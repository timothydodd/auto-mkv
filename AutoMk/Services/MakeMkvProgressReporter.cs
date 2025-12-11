using System;
using System.Globalization;
using System.Threading.Tasks;
using AutoMk.Models;
using AutoMk.Utilities;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace AutoMk.Services;

public class MakeMkvProgressReporter : IDisposable
{
    private readonly ILogger<MakeMkvProgressReporter> _logger;
    private readonly MakeMkvStatus _status = new();
    private string _currentTitle = "";
    private bool _isActive;
    private ProgressTask? _progressTask;
    private ProgressContext? _progressContext;

    public MakeMkvProgressReporter(ILogger<MakeMkvProgressReporter> logger)
    {
        _logger = ValidationHelper.ValidateNotNull(logger);
    }

    public MakeMkvStatus Status => _status;

    /// <summary>
    /// Executes an async operation with Spectre.Console progress display
    /// </summary>
    public async Task<T> RunWithProgressAsync<T>(string title, Func<Action<string>, Task<T>> operation)
    {
        _currentTitle = title;
        _status.Converting = true;
        _isActive = true;

        T result = default!;

        await AnsiConsole.Progress()
            .AutoRefresh(true)
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn(),
            })
            .StartAsync(async ctx =>
            {
                _progressContext = ctx;
                _progressTask = ctx.AddTask($"[cyan]{Markup.Escape(title)}[/]", maxValue: 100);

                // Execute the operation, passing our ParseProgressLine as the output handler
                result = await operation(ParseProgressLine);

                // Ensure progress shows 100% on completion
                _progressTask.Value = 100;
            });

        _isActive = false;
        _progressTask = null;
        _progressContext = null;
        _status.Converting = false;

        AnsiConsole.MarkupLine($"[green]✓ Completed:[/] [white]{Markup.Escape(_currentTitle)}[/]");

        return result;
    }

    public void StartProgress(string title, int maxTicks = 100)
    {
        _currentTitle = title;
        _status.Converting = true;
        _isActive = true;

        // This is called when not using RunWithProgressAsync
        // Fall back to simple output
        AnsiConsole.MarkupLine($"[cyan]Ripping:[/] [white]{Markup.Escape(title)}[/]");
    }

    public void UpdateProgress(float percentage)
    {
        _status.Percentage = percentage;

        if (_progressTask != null)
        {
            _progressTask.Value = percentage;

            // Update description with ETA if available
            if (_status.Estimated != TimeSpan.Zero)
            {
                var eta = _status.Estimated.TotalHours >= 1
                    ? $"{_status.Estimated.Hours:D2}h {_status.Estimated.Minutes:D2}m"
                    : $"{_status.Estimated.Minutes:D2}m {_status.Estimated.Seconds:D2}s";
                _progressTask.Description = $"[cyan]{Markup.Escape(_currentTitle)}[/] [dim](ETA: {eta})[/]";
            }
        }
    }

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

    public void CompleteProgress()
    {
        if (_isActive && _progressTask == null)
        {
            // Only show completion message if not using Spectre Progress
            AnsiConsole.MarkupLine($"[green]✓ Completed:[/] [white]{Markup.Escape(_currentTitle)}[/]");
        }

        _isActive = false;
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
    }
}
