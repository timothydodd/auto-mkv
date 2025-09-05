using System;
using System.Globalization;
using AutoMk.Models;
using AutoMk.Utilities;
using Microsoft.Extensions.Logging;
using ShellProgressBar;

namespace AutoMk.Services;

public class MakeMkvProgressReporter : IDisposable
{
    private readonly ILogger<MakeMkvProgressReporter> _logger;
    private ProgressBar? _progressBar;
    private readonly MakeMkvStatus _status = new();

    public MakeMkvProgressReporter(ILogger<MakeMkvProgressReporter> logger)
    {
        _logger = ValidationHelper.ValidateNotNull(logger);
    }

    public MakeMkvStatus Status => _status;

    public void StartProgress(string title, int maxTicks = 100)
    {
        _progressBar?.Dispose();
        
        var options = new ProgressBarOptions
        {
            ProgressCharacter = '─',
            ProgressBarOnBottom = true,
            ForegroundColor = ConsoleColor.Yellow,
            BackgroundColor = ConsoleColor.DarkYellow,
            BackgroundCharacter = '\u2593'
        };

        _progressBar = new ProgressBar(maxTicks, title, options);
        _status.Converting = true;
    }

    public void UpdateProgress(float percentage)
    {
        if (_progressBar != null)
        {
            var ticks = (int)(percentage / 100.0 * _progressBar.MaxTicks);
            _progressBar.Tick(ticks, $"Progress: {percentage:F1}%");
        }
        
        _status.Percentage = percentage;
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
        _progressBar?.Dispose();
        _progressBar = null;
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
        _progressBar?.Dispose();
    }
}