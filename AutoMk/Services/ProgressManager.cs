using System;
using System.Threading;
using System.Threading.Tasks;
using AutoMk.Interfaces;
using AutoMk.Models;
using AutoMk.Utilities;
using Microsoft.Extensions.Logging;

namespace AutoMk.Services;

/// <summary>
/// Thin facade over <see cref="DashboardRenderer"/> that preserves the <see cref="IProgressManager"/>
/// contract. All rendering state lives in the dashboard; this class exists to keep the existing
/// callers (MakeMkvProgressReporter, FileTransferClient, etc.) unchanged.
/// </summary>
public class ProgressManager : IProgressManager
{
    private readonly ILogger<ProgressManager> _logger;
    private readonly DashboardRenderer _renderer;

    public bool IsActive => _renderer.IsActive;

    public ProgressManager(ILogger<ProgressManager> logger, DashboardRenderer renderer)
    {
        _logger = ValidationHelper.ValidateNotNull(logger);
        _renderer = ValidationHelper.ValidateNotNull(renderer);
    }

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        _renderer.StartAsync(cancellationToken);

    public Task StopAsync() => _renderer.StopAsync();

    public Task PauseAsync() => _renderer.StopAsync();

    public Task ResumeAsync(CancellationToken cancellationToken = default) =>
        _renderer.StartAsync(cancellationToken);

    public ValueTask DisposeAsync() => _renderer.DisposeAsync();

    public Guid CreateProgressTask(string description, double maxValue = 100, string? category = null)
    {
        // Always register the task. If the dashboard isn't active yet (e.g. file transfer
        // fires before the rip starts), state is still captured and becomes visible the
        // next time the dashboard renders.
        return _renderer.CreateTask(description, maxValue, category);
    }

    public void UpdateProgress(Guid taskId, double value, string? description = null)
    {
        if (taskId == Guid.Empty) return;
        _renderer.UpdateTask(taskId, value, description);
    }

    public void IncrementProgress(Guid taskId, double increment)
    {
        if (taskId == Guid.Empty) return;
        _renderer.IncrementTask(taskId, increment);
    }

    public void UpdateProgressBytes(Guid taskId, long bytesTransferred, long totalBytes,
        double? transferRateMBps = null, TimeSpan? timeRemaining = null)
    {
        if (taskId == Guid.Empty) return;
        _renderer.UpdateTaskBytes(taskId, bytesTransferred, totalBytes, transferRateMBps, timeRemaining);
    }

    public void CompleteProgressTask(Guid taskId)
    {
        if (taskId == Guid.Empty) return;
        _renderer.CompleteTask(taskId);
    }

    public void RemoveProgressTask(Guid taskId)
    {
        if (taskId == Guid.Empty) return;
        _renderer.RemoveTask(taskId);
    }

    public void Log(string message, ProgressLogLevel level = ProgressLogLevel.Info)
    {
        // File logger captures via ILogger; dashboard panel is updated directly so Success
        // styling and markup flavor are preserved. DashboardLogger filters this category out
        // to avoid double entries in the panel.
        _logger.Log(MapLevel(level), "{Message}", message);

        if (_renderer.IsActive)
            _renderer.AppendLog(message, level, isMarkup: false);
        else
            WriteFallback(message, level, isMarkup: false);
    }

    public void LogMarkup(string markup, ProgressLogLevel level = ProgressLogLevel.Info)
    {
        _logger.Log(MapLevel(level), "{Message}", markup);

        if (_renderer.IsActive)
            _renderer.AppendLog(markup, level, isMarkup: true);
        else
            WriteFallback(markup, level, isMarkup: true);
    }

    private static void WriteFallback(string message, ProgressLogLevel level, bool isMarkup)
    {
        var (label, style) = level switch
        {
            ProgressLogLevel.Debug => ("DBG ", "dim"),
            ProgressLogLevel.Warning => ("WARN", "yellow"),
            ProgressLogLevel.Error => ("ERR ", "red"),
            ProgressLogLevel.Success => ("OK  ", "green"),
            _ => ("INFO", "blue")
        };
        var body = isMarkup ? message : Spectre.Console.Markup.Escape(message);
        Spectre.Console.AnsiConsole.MarkupLine($"[{style}]{label}[/] {body}");
    }

    public void SetStatus(string status) => _logger.LogDebug("Status: {Status}", status);

    public void ClearStatus() { }

    private static LogLevel MapLevel(ProgressLogLevel level) => level switch
    {
        ProgressLogLevel.Debug => LogLevel.Debug,
        ProgressLogLevel.Info => LogLevel.Information,
        ProgressLogLevel.Warning => LogLevel.Warning,
        ProgressLogLevel.Error => LogLevel.Error,
        ProgressLogLevel.Success => LogLevel.Information,
        _ => LogLevel.Information
    };
}
