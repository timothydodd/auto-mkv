using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AutoMk.Interfaces;
using AutoMk.Models;
using Microsoft.Extensions.Logging;

namespace AutoMk.Services;

/// <summary>
/// Scans the output tree for new .mkv files and hands them to the transfer queue. The actual
/// uploads happen in <see cref="FileTransferBackgroundService"/> so they run independently of
/// the rip loop — this service is only a producer.
/// </summary>
public class MediaMoverService : IMediaMoverService
{
    readonly IFileTransferQueue _transferQueue;
    readonly FileTransferSettings _fileTransferSettings;
    readonly ILogger<MediaMoverService> _logger;
    readonly HashSet<string> _processedFiles = new(StringComparer.OrdinalIgnoreCase);

    public MediaMoverService(FileTransferSettings fileTransferSettings, IFileTransferQueue transferQueue, ILogger<MediaMoverService> logger)
    {
        _fileTransferSettings = fileTransferSettings;
        _transferQueue = transferQueue;
        _logger = logger;
    }

    public async Task FindFiles(string outputPath)
    {
        if (_fileTransferSettings.Enabled is false)
        {
            _logger.LogDebug("File transfer is disabled, skipping file scan");
            return;
        }

        _logger.LogDebug("Scanning for files to transfer in: {OutputPath}", outputPath);

        // Find all .mkv files in subdirectories (exclude files in root output folder)
        var allFiles = Directory.GetFiles(outputPath, "*.mkv", SearchOption.AllDirectories);
        _logger.LogDebug("Found {FileCount} .mkv files total", allFiles.Length);

        var filesQueued = 0;
        foreach (var filePath in allFiles)
        {
            if (_processedFiles.Contains(filePath))
            {
                continue;
            }

            // Skip files that are directly in the root output folder
            var fileDirectory = Path.GetDirectoryName(filePath) ?? string.Empty;
            if (Path.GetFullPath(fileDirectory).Equals(Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Skip files in _moved folders
            if (filePath.Contains("_moved", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(outputPath, filePath);
            filesQueued++;

            _logger.LogInformation("Queued transfer: {RelativePath}", relativePath);
            await _transferQueue.EnqueueAsync(new FileTransferJob(relativePath, filePath));
            _processedFiles.Add(filePath);
        }

        if (filesQueued > 0)
        {
            _logger.LogInformation("Queued {FileCount} file(s) for transfer", filesQueued);
        }
        else
        {
            _logger.LogDebug("No new files found for transfer");
        }
    }
}
