using System;
using AutoMk.Interfaces;
using AutoMk.Models;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace AutoMk.Services;

/// <summary>
/// Provides enhanced console output functionality for user feedback using Spectre.Console
/// </summary>
public class ConsoleOutputService : IConsoleOutputService
{
    private readonly ILogger<ConsoleOutputService> _logger;
    private readonly bool _enableConsoleOutput;

    public ConsoleOutputService(ILogger<ConsoleOutputService> logger, RipSettings ripSettings)
    {
        _logger = logger;
        _enableConsoleOutput = ripSettings.ShowProgressMessages;
    }

    /// <summary>
    /// Outputs a disc detection message
    /// </summary>
    public void ShowDiscDetected(string discName)
    {
        if (_enableConsoleOutput)
        {
            AnsiConsole.MarkupLine($"[blue]Detected disc:[/] [white]{Markup.Escape(discName)}[/]");
        }
        _logger.LogInformation($"Detected disc: {discName}");
    }

    /// <summary>
    /// Outputs a processing started message
    /// </summary>
    public void ShowProcessingStarted(string discName)
    {
        if (_enableConsoleOutput)
        {
            AnsiConsole.MarkupLine($"[cyan]Starting to process disc:[/] [white]{Markup.Escape(discName)}[/]");
        }
        _logger.LogInformation($"Starting to process disc: {discName}");
    }

    /// <summary>
    /// Outputs a media identification result
    /// </summary>
    public void ShowMediaIdentified(string title, string type)
    {
        if (_enableConsoleOutput)
        {
            AnsiConsole.MarkupLine($"[green]Identified as:[/] [white]{Markup.Escape(title)}[/] [dim]({Markup.Escape(type)})[/]");
        }
        _logger.LogInformation($"Identified as: {title} ({type})");
    }

    /// <summary>
    /// Outputs ripping progress information
    /// </summary>
    public void ShowRippingProgress(int totalTitles, string discName)
    {
        if (_enableConsoleOutput)
        {
            AnsiConsole.MarkupLine($"[cyan]Ripping[/] [yellow]{totalTitles}[/] [cyan]titles from disc:[/] [white]{Markup.Escape(discName)}[/]");
        }
        _logger.LogInformation($"Ripping {totalTitles} titles from disc: {discName}");
    }

    /// <summary>
    /// Outputs current title being ripped
    /// </summary>
    public void ShowCurrentTitleRipping(int currentTitle, int totalTitles, string titleName, string titleId)
    {
        if (_enableConsoleOutput)
        {
            AnsiConsole.MarkupLine($"   [dim]Ripping title[/] [yellow]{currentTitle}/{totalTitles}[/][dim]:[/] [white]{Markup.Escape(titleName)}[/] [dim](ID: {Markup.Escape(titleId)})[/]");
        }
        _logger.LogInformation($"Ripping title {currentTitle}/{totalTitles}: {titleName} (ID: {titleId})");
    }

    /// <summary>
    /// Outputs ripping completion message
    /// </summary>
    public void ShowRippingCompleted()
    {
        if (_enableConsoleOutput)
        {
            AnsiConsole.MarkupLine("[green]Ripping completed successfully[/]");
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
            AnsiConsole.MarkupLine($"[yellow]WARNING:[/] [white]{Markup.Escape(message)}[/]");
        }
        _logger.LogWarning(message);
    }

    /// <summary>
    /// Outputs error messages (always shown regardless of setting)
    /// </summary>
    public void ShowError(string message)
    {
        // Errors are always shown
        AnsiConsole.MarkupLine($"[red]ERROR:[/] [white]{Markup.Escape(message)}[/]");
        _logger.LogError(message);
    }

    /// <summary>
    /// Outputs file rename information
    /// </summary>
    public void ShowFileRenamed(string oldName, string newName)
    {
        if (_enableConsoleOutput)
        {
            AnsiConsole.MarkupLine($"[dim]Renamed:[/] [white]{Markup.Escape(oldName)}[/] [blue]->[/] [green]{Markup.Escape(newName)}[/]");
        }
        _logger.LogInformation($"Renamed: {oldName} -> {newName}");
    }

    /// <summary>
    /// Outputs file organization completion
    /// </summary>
    public void ShowOrganizationCompleted(int fileCount)
    {
        if (_enableConsoleOutput)
        {
            AnsiConsole.MarkupLine($"[green]Successfully organized[/] [yellow]{fileCount}[/] [green]files[/]");
        }
        _logger.LogInformation($"Successfully organized {fileCount} files");
    }

    /// <summary>
    /// Outputs file move destination
    /// </summary>
    public void ShowFilesMovedTo(string destination)
    {
        if (_enableConsoleOutput)
        {
            AnsiConsole.MarkupLine($"[dim]Files moved to:[/] [cyan]{Markup.Escape(destination)}[/]");
        }
        _logger.LogInformation($"Files moved to: {destination}");
    }

    /// <summary>
    /// Outputs general information messages
    /// </summary>
    public void ShowInfo(string message)
    {
        if (_enableConsoleOutput)
        {
            AnsiConsole.MarkupLine($"[blue]INFO:[/] [white]{Markup.Escape(message)}[/]");
        }
        _logger.LogInformation(message);
    }

    /// <summary>
    /// Outputs success messages
    /// </summary>
    public void ShowSuccess(string message)
    {
        if (_enableConsoleOutput)
        {
            AnsiConsole.MarkupLine($"[green]SUCCESS:[/] [white]{Markup.Escape(message)}[/]");
        }
        _logger.LogInformation(message);
    }

    /// <summary>
    /// Outputs file transfer start message
    /// </summary>
    public void ShowFileTransferStarted(string fileName, string destination)
    {
        if (_enableConsoleOutput)
        {
            AnsiConsole.MarkupLine($"[cyan]Starting transfer:[/] [white]{Markup.Escape(fileName)}[/] [blue]->[/] [dim]{Markup.Escape(destination)}[/]");
        }
        _logger.LogInformation($"Starting transfer: {fileName} -> {destination}");
    }

    /// <summary>
    /// Outputs file transfer completion message
    /// </summary>
    public void ShowFileTransferCompleted(string fileName)
    {
        if (_enableConsoleOutput)
        {
            AnsiConsole.MarkupLine($"[green]Transfer completed:[/] [white]{Markup.Escape(fileName)}[/]");
        }
        _logger.LogInformation($"Transfer completed: {fileName}");
    }

    /// <summary>
    /// Outputs file transfer progress with time remaining
    /// </summary>
    public void ShowFileTransferProgress(string fileName, long bytesTransferred, long totalBytes, TimeSpan timeRemaining, double transferRateMBps)
    {
        if (_enableConsoleOutput)
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
            AnsiConsole.Markup($"\r[dim]{Markup.Escape(fileName)}[/] [{progressBar}] [yellow]{percentage:F1}%[/] [dim]({transferredGB:F2}/{totalGB:F2} GB)[/] [cyan]{transferRateMBps:F1} MB/s[/] [dim]ETA:[/] [white]{timeRemainingStr}[/]    ");
        }
    }
}
