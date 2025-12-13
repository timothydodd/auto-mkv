using System;
using System.Globalization;
using System.Threading.Tasks;
using AutoMk.Interfaces;
using AutoMk.Models;
using AutoMk.Utilities;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace AutoMk.Services;

/// <summary>
/// Handles progress reporting for MakeMKV operations.
/// Integrates with ProgressManager for concurrent progress bar display.
/// </summary>
public class MakeMkvProgressReporter : IDisposable
{
    private readonly ILogger<MakeMkvProgressReporter> _logger;
    private readonly IProgressManager _progressManager;
    private readonly MakeMkvStatus _status = new();
    private string _currentTitle = "";
    private bool _isActive;
    private Guid? _currentTaskId;

    public MakeMkvProgressReporter(
        ILogger<MakeMkvProgressReporter> logger,
        IProgressManager progressManager)
    {
        _logger = ValidationHelper.ValidateNotNull(logger);
        _progressManager = ValidationHelper.ValidateNotNull(progressManager);
    }

    public MakeMkvStatus Status => _status;

    /// <summary>
    /// Executes an async operation with progress tracking via ProgressManager.
    /// </summary>
    public async Task<T> RunWithProgressAsync<T>(string title, Func<Action<string>, Task<T>> operation)
    {
        _currentTitle = title;
        _status.Converting = true;
        _isActive = true;

        // Create progress task via ProgressManager
        _currentTaskId = _progressManager.CreateProgressTask(
            $"Ripping: {title}",
            maxValue: 100,
            category: "MakeMKV");

        T result = default!;

        try
        {
            // Execute the operation, passing our ParseProgressLine as the output handler
            result = await operation(ParseProgressLine);

            // Ensure progress shows 100% on completion
            if (_currentTaskId.HasValue)
            {
                _progressManager.CompleteProgressTask(_currentTaskId.Value);
            }

            _progressManager.Log($"Completed: {title}", ProgressLogLevel.Success);
        }
        catch (Exception)
        {
            // Remove progress task on error
            if (_currentTaskId.HasValue)
            {
                _progressManager.RemoveProgressTask(_currentTaskId.Value);
            }
            throw;
        }
        finally
        {
            _isActive = false;
            _currentTaskId = null;
            _status.Converting = false;
        }

        return result;
    }

    /// <summary>
    /// Legacy method for starting progress without async wrapper.
    /// Outputs to ProgressManager log area.
    /// </summary>
    public void StartProgress(string title, int maxTicks = 100)
    {
        _currentTitle = title;
        _status.Converting = true;
        _isActive = true;

        // Create progress task if ProgressManager is active
        if (_progressManager.IsActive)
        {
            _currentTaskId = _progressManager.CreateProgressTask(
                $"Ripping: {title}",
                maxValue: maxTicks,
                category: "MakeMKV");
        }
        else
        {
            // Fallback to simple output
            AnsiConsole.MarkupLine($"[cyan]Ripping:[/] [white]{Markup.Escape(title)}[/]");
        }
    }

    /// <summary>
    /// Updates progress percentage and ETA display.
    /// </summary>
    public void UpdateProgress(float percentage)
    {
        _status.Percentage = percentage;

        if (_currentTaskId.HasValue)
        {
            var description = $"Ripping: {_currentTitle}";
            if (_status.Estimated != TimeSpan.Zero)
            {
                var eta = _status.Estimated.TotalHours >= 1
                    ? $"{_status.Estimated.Hours:D2}h {_status.Estimated.Minutes:D2}m"
                    : $"{_status.Estimated.Minutes:D2}m {_status.Estimated.Seconds:D2}s";
                description = $"Ripping: {_currentTitle} (ETA: {eta})";
            }

            _progressManager.UpdateProgress(_currentTaskId.Value, percentage, description);
        }
    }

    /// <summary>
    /// Parses MakeMKV progress output lines (PRGV: and PRGT:).
    /// </summary>
    public void ParseProgressLine(string line)
    {
        if (string.IsNullOrEmpty(line))
            return;

        try
        {
            if (line.StartsWith("PRGV:"))
            {
                var parts = line.Split(',');
                if (parts.Length >= 3 && int.TryParse(parts[1], out var current) && int.TryParse(parts[2], out var total))
                {
                    var percentage = total > 0 ? (float)current / total * 100 : 0;
                    UpdateProgress(percentage);
                }
            }
            else if (line.StartsWith("PRGT:"))
            {
                var parts = line.Split(',');
                if (parts.Length >= 4)
                {
                    if (float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var currentFps))
                        _status.CurrentFps = currentFps;

                    if (float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var avgFps))
                        _status.AverageFps = avgFps;

                    if (TimeSpan.TryParse(parts[3], out var estimated))
                        _status.Estimated = estimated;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse progress line: {Line}", line);
        }
    }

    /// <summary>
    /// Completes the current progress operation.
    /// </summary>
    public void CompleteProgress()
    {
        if (_isActive)
        {
            if (_currentTaskId.HasValue)
            {
                _progressManager.CompleteProgressTask(_currentTaskId.Value);
            }
            else if (!_progressManager.IsActive)
            {
                // Only show completion message if not using ProgressManager
                AnsiConsole.MarkupLine($"[green]✓ Completed:[/] [white]{Markup.Escape(_currentTitle)}[/]");
            }
        }

        _isActive = false;
        _currentTaskId = null;
        _status.Converting = false;
        _status.Percentage = 0;
        _status.CurrentFps = 0;
        _status.AverageFps = 0;
        _status.Estimated = TimeSpan.Zero;
        _status.InputFile = null;
        _status.OutputFile = null;
    }

    public void Dispose()
    {
        _isActive = false;
        if (_currentTaskId.HasValue)
        {
            _progressManager.RemoveProgressTask(_currentTaskId.Value);
            _currentTaskId = null;
        }
    }
}
