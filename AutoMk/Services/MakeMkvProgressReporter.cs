using System;
using System.Globalization;
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
    private float _lastPercentage = -1;

    public MakeMkvProgressReporter(ILogger<MakeMkvProgressReporter> logger)
    {
        _logger = ValidationHelper.ValidateNotNull(logger);
    }

    public MakeMkvStatus Status => _status;

    public void StartProgress(string title, int maxTicks = 100)
    {
        _currentTitle = title;
        _status.Converting = true;
        _isActive = true;
        _lastPercentage = -1;

        AnsiConsole.MarkupLine($"[cyan]Starting:[/] [white]{Markup.Escape(title)}[/]");
    }

    public void UpdateProgress(float percentage)
    {
        _status.Percentage = percentage;

        if (_isActive)
        {
            // Only update display if percentage changed significantly (reduces flicker)
            if (Math.Abs(percentage - _lastPercentage) < 0.5f && _lastPercentage >= 0)
                return;

            _lastPercentage = percentage;

            // Build status line with available information
            var statusParts = new System.Collections.Generic.List<string>();
            statusParts.Add($"[yellow]{percentage:F1}%[/]");

            if (_status.CurrentFps > 0)
                statusParts.Add($"[dim]{_status.CurrentFps:F1} fps[/]");

            if (_status.Estimated != TimeSpan.Zero)
            {
                var eta = _status.Estimated.TotalHours >= 1
                    ? $"{_status.Estimated.Hours:D2}h {_status.Estimated.Minutes:D2}m"
                    : $"{_status.Estimated.Minutes:D2}m {_status.Estimated.Seconds:D2}s";
                statusParts.Add($"[cyan]ETA: {eta}[/]");
            }

            var statusLine = string.Join(" [dim]|[/] ", statusParts);

            // Create a colorful progress bar representation
            var progressWidth = 30;
            var filledWidth = (int)(percentage / 100.0 * progressWidth);
            var emptyWidth = progressWidth - filledWidth;
            var progressBar = $"[green]{new string('━', filledWidth)}[/][dim]{new string('─', emptyWidth)}[/]";

            // Use carriage return to update in place with markup
            AnsiConsole.Markup($"\r  [{progressBar}] {statusLine}          ");
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
        if (_isActive)
        {
            // Clear the progress line and show completion
            AnsiConsole.WriteLine(); // Move to next line
            AnsiConsole.MarkupLine($"[green]Completed:[/] [white]{Markup.Escape(_currentTitle)}[/]");
        }

        _isActive = false;
        _lastPercentage = -1;
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
        if (_isActive)
        {
            AnsiConsole.WriteLine(); // Ensure we're on a new line
        }
        _isActive = false;
    }
}
