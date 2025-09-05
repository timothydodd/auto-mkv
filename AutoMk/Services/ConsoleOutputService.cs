using System;
using AutoMk.Interfaces;
using AutoMk.Models;
using Microsoft.Extensions.Logging;

namespace AutoMk.Services;

/// <summary>
/// Provides enhanced console output functionality for user feedback
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
            Console.WriteLine($"Detected disc: {discName}");
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
            Console.WriteLine($"Starting to process disc: {discName}");
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
            Console.WriteLine($"Identified as: {title} ({type})");
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
            Console.WriteLine($"Ripping {totalTitles} titles from disc: {discName}");
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
            Console.WriteLine($"   Ripping title {currentTitle}/{totalTitles}: {titleName} (ID: {titleId})");
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
            Console.WriteLine("Ripping completed successfully");
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
            Console.WriteLine($"WARNING: {message}");
        }
        _logger.LogWarning(message);
    }

    /// <summary>
    /// Outputs error messages (always shown regardless of setting)
    /// </summary>
    public void ShowError(string message)
    {
        // Errors are always shown
        Console.WriteLine($"ERROR: {message}");
        _logger.LogError(message);
    }

    /// <summary>
    /// Outputs file rename information
    /// </summary>
    public void ShowFileRenamed(string oldName, string newName)
    {
        if (_enableConsoleOutput)
        {
            Console.WriteLine($"Renamed: {oldName} -> {newName}");
        }
        _logger.LogInformation($"Renamed: {oldName} → {newName}");
    }

    /// <summary>
    /// Outputs file organization completion
    /// </summary>
    public void ShowOrganizationCompleted(int fileCount)
    {
        if (_enableConsoleOutput)
        {
            Console.WriteLine($"Successfully organized {fileCount} files");
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
            Console.WriteLine($"Files moved to: {destination}");
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
            Console.WriteLine($"INFO: {message}");
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
            Console.WriteLine($"SUCCESS: {message}");
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
            Console.WriteLine($"Starting transfer: {fileName} -> {destination}");
        }
        _logger.LogInformation($"Starting transfer: {fileName} → {destination}");
    }

    /// <summary>
    /// Outputs file transfer completion message
    /// </summary>
    public void ShowFileTransferCompleted(string fileName)
    {
        if (_enableConsoleOutput)
        {
            Console.WriteLine($"Transfer completed: {fileName}");
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

            // Clear the current line and write the progress
            Console.Write($"\r{fileName}: {percentage:F1}% ({transferredGB:F2}/{totalGB:F2} GB) - {transferRateMBps:F1} MB/s - ETA: {timeRemainingStr}");
        }
    }
}