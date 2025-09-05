using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AutoMk.Interfaces;
using AutoMk.Models;

namespace AutoMk.Services;

public class MediaMoverService : IMediaMoverService
{
    readonly IFileTransferClient _fileTransferClient;
    readonly FileTransferSettings _fileTransferSettings;
    readonly HashSet<string> _processedFiles = new(StringComparer.OrdinalIgnoreCase);

    public MediaMoverService(FileTransferSettings fileTransferSettings, IFileTransferClient fileTransferClient)
    {
        _fileTransferSettings = fileTransferSettings;
        _fileTransferClient = fileTransferClient;
    }

    public async Task FindFiles(string outputPath)
    {
        if (_fileTransferSettings.Enabled is false)
            return;

        // Find all .mkv files in subdirectories (exclude files in root output folder)
        var allFiles = Directory.GetFiles(outputPath, "*.mkv", SearchOption.AllDirectories);

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

            await _fileTransferClient.SendFileInBackground(relativePath, filePath);

            _processedFiles.Add(filePath);
        }


    }
}
