using System;
using AutoMk.Interfaces;
using AutoMk.Models;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace AutoMk.Services;

/// <summary>
/// Provides enhanced console output functionality for user feedback using Spectre.Console.
/// When ProgressManager is active, routes messages through its scrolling log area.
/// </summary>
public class ConsoleOutputService : IConsoleOutputService
{
    private readonly ILogger<ConsoleOutputService> _logger;
    private readonly IProgressManager _progressManager;
    private readonly bool _enableConsoleOutput;

    public ConsoleOutputService(
        ILogger<ConsoleOutputService> logger,
        RipSettings ripSettings,
        IProgressManager progressManager)
    {
        _logger = logger;
        _progressManager = progressManager;
        _enableConsoleOutput = ripSettings.ShowProgressMessages;
    }

    /// <summary>
    /// Outputs a disc detection message
    /// </summary>
    public void ShowDiscDetected(string discName)
    {
        if (_enableConsoleOutput)
        {
            OutputMessage($"[blue]Detected disc:[/] [white]{Markup.Escape(discName)}[/]", ProgressLogLevel.Info);
        }
        _logger.LogInformation("Detected disc: {DiscName}", discName);
    }

    /// <summary>
    /// Outputs a processing started message
    /// </summary>
    public void ShowProcessingStarted(string discName)
    {
        if (_enableConsoleOutput)
        {
            OutputMessage($"[cyan]Starting to process disc:[/] [white]{Markup.Escape(discName)}[/]", ProgressLogLevel.Info);
        }
        _logger.LogInformation("Starting to process disc: {DiscName}", discName);
    }

    /// <summary>
    /// Outputs a media identification result
    /// </summary>
    public void ShowMediaIdentified(string title, string type)
    {
        if (_enableConsoleOutput)
        {
            OutputMessage($"[green]Identified as:[/] [white]{Markup.Escape(title)}[/] [dim]({Markup.Escape(type)})[/]", ProgressLogLevel.Success);
        }
        _logger.LogInformation("Identified as: {Title} ({Type})", title, type);
    }

    /// <summary>
    /// Outputs ripping progress information
    /// </summary>
    public void ShowRippingProgress(int totalTitles, string discName)
    {
        if (_enableConsoleOutput)
        {
            OutputMessage($"[cyan]Ripping[/] [yellow]{totalTitles}[/] [cyan]titles from disc:[/] [white]{Markup.Escape(discName)}[/]", ProgressLogLevel.Info);
        }
        _logger.LogInformation("Ripping {TotalTitles} titles from disc: {DiscName}", totalTitles, discName);
    }

    /// <summary>
    /// Outputs current title being ripped
    /// </summary>
    public void ShowCurrentTitleRipping(int currentTitle, int totalTitles, string titleName, string titleId)
    {
        if (_enableConsoleOutput)
        {
            OutputMessage($"   [dim]Ripping title[/] [yellow]{currentTitle}/{totalTitles}[/][dim]:[/] [white]{Markup.Escape(titleName)}[/] [dim](ID: {Markup.Escape(titleId)})[/]", ProgressLogLevel.Info);
        }
        _logger.LogInformation("Ripping title {CurrentTitle}/{TotalTitles}: {TitleName} (ID: {TitleId})", currentTitle, totalTitles, titleName, titleId);
    }

    /// <summary>
    /// Outputs ripping completion message
    /// </summary>
    public void ShowRippingCompleted()
    {
        if (_enableConsoleOutput)
        {
            OutputMessage("[green]Ripping completed successfully[/]", ProgressLogLevel.Success);
        }
        _logger.LogInformation("Ripping completed successfully");
    }

    /// <summary>
    /// Outputs warning messages
    /// </summary>
    public void ShowWarning(string message)
    {
        if (_enableConsoleOutput)
        {
            OutputMessage($"[yellow]WARNING:[/] [white]{Markup.Escape(message)}[/]", ProgressLogLevel.Warning);
        }
        _logger.LogWarning("{Message}", message);
    }

    /// <summary>
    /// Outputs error messages (always shown regardless of setting)
    /// </summary>
    public void ShowError(string message)
    {
        // Errors are always shown
        OutputMessage($"[red]ERROR:[/] [white]{Markup.Escape(message)}[/]", ProgressLogLevel.Error);
        _logger.LogError("{Message}", message);
    }

    /// <summary>
    /// Outputs file rename information
    /// </summary>
    public void ShowFileRenamed(string oldName, string newName)
    {
        if (_enableConsoleOutput)
        {
            OutputMessage($"[dim]Renamed:[/] [white]{Markup.Escape(oldName)}[/] [blue]->[/] [green]{Markup.Escape(newName)}[/]", ProgressLogLevel.Info);
        }
        _logger.LogInformation("Renamed: {OldName} -> {NewName}", oldName, newName);
    }

    /// <summary>
    /// Outputs file organization completion
    /// </summary>
    public void ShowOrganizationCompleted(int fileCount)
    {
        if (_enableConsoleOutput)
        {
            OutputMessage($"[green]Successfully organized[/] [yellow]{fileCount}[/] [green]files[/]", ProgressLogLevel.Success);
        }
        _logger.LogInformation("Successfully organized {FileCount} files", fileCount);
    }

    /// <summary>
    /// Outputs file move destination
    /// </summary>
    public void ShowFilesMovedTo(string destination)
    {
        if (_enableConsoleOutput)
        {
            OutputMessage($"[dim]Files moved to:[/] [cyan]{Markup.Escape(destination)}[/]", ProgressLogLevel.Info);
        }
        _logger.LogInformation("Files moved to: {Destination}", destination);
    }

    /// <summary>
    /// Outputs general information messages
    /// </summary>
    public void ShowInfo(string message)
    {
        if (_enableConsoleOutput)
        {
            OutputMessage($"[blue]INFO:[/] [white]{Markup.Escape(message)}[/]", ProgressLogLevel.Info);
        }
        _logger.LogInformation("{Message}", message);
    }

    /// <summary>
    /// Outputs success messages
    /// </summary>
    public void ShowSuccess(string message)
    {
        if (_enableConsoleOutput)
        {
            OutputMessage($"[green]SUCCESS:[/] [white]{Markup.Escape(message)}[/]", ProgressLogLevel.Success);
        }
        _logger.LogInformation("{Message}", message);
    }

    /// <summary>
    /// Outputs file transfer start message
    /// </summary>
    public void ShowFileTransferStarted(string fileName, string destination)
    {
        if (_enableConsoleOutput)
        {
            OutputMessage($"[cyan]Starting transfer:[/] [white]{Markup.Escape(fileName)}[/] [blue]->[/] [dim]{Markup.Escape(destination)}[/]", ProgressLogLevel.Info);
        }
        _logger.LogInformation("Starting transfer: {FileName} -> {Destination}", fileName, destination);
    }

    /// <summary>
    /// Outputs file transfer completion message
    /// </summary>
    public void ShowFileTransferCompleted(string fileName)
    {
        if (_enableConsoleOutput)
        {
            OutputMessage($"[green]Transfer completed:[/] [white]{Markup.Escape(fileName)}[/]", ProgressLogLevel.Success);
        }
        _logger.LogInformation("Transfer completed: {FileName}", fileName);
    }

    /// <summary>
    /// Outputs file transfer progress with time remaining.
    /// Note: When ProgressManager is active, file transfer progress is handled via progress tasks,
    /// so this method only outputs when ProgressManager is not active (legacy fallback).
    /// </summary>
    public void ShowFileTransferProgress(string fileName, long bytesTransferred, long totalBytes, TimeSpan timeRemaining, double transferRateMBps)
    {
        // When ProgressManager is active, file transfers use progress tasks instead
        // This fallback is only used when ProgressManager is not running
        if (!_progressManager.IsActive && _enableConsoleOutput)
        {
            var percentage = totalBytes > 0 ? (bytesTransferred * 100.0 / totalBytes) : 0;
            var transferredGB = bytesTransferred / (1024.0 * 1024.0 * 1024.0);
            var totalGB = totalBytes / (1024.0 * 1024.0 * 1024.0);

            var timeRemainingStr = timeRemaining.TotalHours >= 1
                ? $"{timeRemaining.Hours:D2}h {timeRemaining.Minutes:D2}m {timeRemaining.Seconds:D2}s"
                : $"{timeRemaining.Minutes:D2}m {timeRemaining.Seconds:D2}s";

            // Create a colorful progress bar
            var progressWidth = 25;
            var filledWidth = (int)(percentage / 100.0 * progressWidth);
            var emptyWidth = progressWidth - filledWidth;
            var progressBar = $"[green]{new string('━', filledWidth)}[/][dim]{new string('─', emptyWidth)}[/]";

            // Use carriage return to update in place with Spectre markup
            AnsiConsole.Markup($"\r[dim]{Markup.Escape(fileName)}[/] [[{progressBar}]] [yellow]{percentage:F1}%[/] [dim]({transferredGB:F2}/{totalGB:F2} GB)[/] [cyan]{transferRateMBps:F1} MB/s[/] [dim]ETA:[/] [white]{timeRemainingStr}[/]    ");
        }
    }

    /// <summary>
    /// Routes output through ProgressManager when active, otherwise uses direct console output.
    /// </summary>
    private void OutputMessage(string markup, ProgressLogLevel level)
    {
        if (_progressManager.IsActive)
        {
            // Route through ProgressManager's scrolling log area
            _progressManager.LogMarkup(markup, level);
        }
        else
        {
            // Direct console output (fallback)
            AnsiConsole.MarkupLine(markup);
        }
    }
}
