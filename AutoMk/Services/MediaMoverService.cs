using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AutoMk.Interfaces;
using AutoMk.Models;
using Microsoft.Extensions.Logging;

namespace AutoMk.Services;

public class MediaMoverService : IMediaMoverService
{
    readonly IFileTransferClient _fileTransferClient;
    readonly FileTransferSettings _fileTransferSettings;
    readonly ILogger<MediaMoverService> _logger;
    readonly HashSet<string> _processedFiles = new(StringComparer.OrdinalIgnoreCase);

    public MediaMoverService(FileTransferSettings fileTransferSettings, IFileTransferClient fileTransferClient, ILogger<MediaMoverService> logger)
    {
        _fileTransferSettings = fileTransferSettings;
        _fileTransferClient = fileTransferClient;
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

        var filesToTransfer = 0;
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

            //get relative path
            var relativePath = Path.GetRelativePath(outputPath, filePath);
            filesToTransfer++;

            _logger.LogInformation("Starting background transfer for file: {RelativePath}", relativePath);

            // Fire and forget - don't await this to prevent blocking the main loop
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await _fileTransferClient.SendFileInBackground(relativePath, filePath);
                    if (result == true)
                    {
                        _logger.LogInformation("Successfully transferred file: {RelativePath}", relativePath);
                    }
                    else if (result == false)
                    {
                        _logger.LogWarning("Failed to transfer file: {RelativePath}", relativePath);
                    }
                    else
                    {
                        _logger.LogDebug("File transfer skipped (disabled): {RelativePath}", relativePath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error transferring file: {RelativePath}", relativePath);
                }
            });

            _processedFiles.Add(filePath);
        }

        if (filesToTransfer > 0)
        {
            _logger.LogInformation("Started background transfer for {FileCount} files", filesToTransfer);
        }
        else
        {
            _logger.LogDebug("No new files found for transfer");
        }
    }
}
