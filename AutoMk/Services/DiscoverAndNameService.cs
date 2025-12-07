using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutoMk.Interfaces;
using AutoMk.Models;
using AutoMk.Utilities;
using Microsoft.Extensions.Logging;

namespace AutoMk.Services;

/// <summary>
/// Service for discovering existing MKV files and organizing them without ripping
/// </summary>
public class DiscoverAndNameService : IDiscoverAndNameService
{
    private readonly RipSettings _ripSettings;
    private readonly ILogger<DiscoverAndNameService> _logger;
    private readonly IConsolePromptService _promptService;
    private readonly IMediaSelectionService _mediaSelectionService;
    private readonly IMediaNamingService _namingService;
    private readonly IMediaMoverService _moverService;
    private readonly IEnhancedOmdbService _omdbService;
    private readonly IConsoleOutputService _consoleOutput;
    private readonly IFileTransferClient _fileTransferClient;
    private readonly FileTransferSettings _fileTransferSettings;

    public DiscoverAndNameService(
        RipSettings ripSettings,
        ILogger<DiscoverAndNameService> logger,
        IConsolePromptService promptService,
        IMediaSelectionService mediaSelectionService,
        IMediaNamingService namingService,
        IMediaMoverService moverService,
        IEnhancedOmdbService omdbService,
        IConsoleOutputService consoleOutput,
        IFileTransferClient fileTransferClient,
        FileTransferSettings fileTransferSettings)
    {
        _ripSettings = ValidationHelper.ValidateNotNull(ripSettings);
        _logger = ValidationHelper.ValidateNotNull(logger);
        _promptService = ValidationHelper.ValidateNotNull(promptService);
        _mediaSelectionService = ValidationHelper.ValidateNotNull(mediaSelectionService);
        _namingService = ValidationHelper.ValidateNotNull(namingService);
        _moverService = ValidationHelper.ValidateNotNull(moverService);
        _omdbService = ValidationHelper.ValidateNotNull(omdbService);
        _consoleOutput = ValidationHelper.ValidateNotNull(consoleOutput);
        _fileTransferClient = ValidationHelper.ValidateNotNull(fileTransferClient);
        _fileTransferSettings = ValidationHelper.ValidateNotNull(fileTransferSettings);
    }

    public async Task RunDiscoverAndNameWorkflowAsync()
    {
        try
        {
            _promptService.DisplayHeader("DISCOVER AND NAME MODE");
            Console.WriteLine("This mode will search for existing MKV files and help you organize them.");
            Console.WriteLine();

            // Prompt for input directory
            var directoryResult = _promptService.TextPrompt(new TextPromptOptions
            {
                Question = "Enter the directory path to search for MKV files:",
                Required = true,
                PromptText = "Directory Path",
                DefaultValue = _ripSettings.Output
            });

            if (!directoryResult.Success || directoryResult.Cancelled)
            {
                _logger.LogInformation("User cancelled directory selection");
                return;
            }

            var searchDirectory = directoryResult.Value;

            // Validate directory exists
            if (!Directory.Exists(searchDirectory))
            {
                _consoleOutput.ShowError($"Directory does not exist: {searchDirectory}");
                _logger.LogError($"Directory does not exist: {searchDirectory}");
                return;
            }

            // Find all MKV files
            _logger.LogInformation($"Searching for MKV files in: {searchDirectory}");
            var mkvFiles = Directory.GetFiles(searchDirectory, "*.mkv", SearchOption.AllDirectories)
                .OrderBy(f => f)
                .ToList();

            if (mkvFiles.Count == 0)
            {
                _consoleOutput.ShowWarning("No MKV files found in the specified directory.");
                _logger.LogInformation("No MKV files found");
                return;
            }

            Console.WriteLine($"Found {mkvFiles.Count} MKV file(s)");
            Console.WriteLine();

            // Process each file
            int processedCount = 0;
            int skippedCount = 0;

            foreach (var filePath in mkvFiles)
            {
                var fileName = Path.GetFileName(filePath);
                var fileInfo = new FileInfo(filePath);
                var fileSizeGB = fileInfo.Length / (1024.0 * 1024.0 * 1024.0);

                Console.WriteLine();
                _promptService.DisplayHeader($"File {processedCount + skippedCount + 1} of {mkvFiles.Count}");
                Console.WriteLine($"File: {fileName}");
                Console.WriteLine($"Size: {fileSizeGB:F2} GB");
                Console.WriteLine($"Path: {filePath}");
                Console.WriteLine();

                // Ask if file needs to be named
                var needsNamingResult = _promptService.ConfirmPrompt(new ConfirmPromptOptions
                {
                    Question = "Does this file need to be identified and named?",
                    DefaultValue = true
                });

                if (!needsNamingResult.Success || needsNamingResult.Cancelled)
                {
                    _logger.LogInformation("User cancelled processing");
                    break;
                }

                if (needsNamingResult.Value)
                {
                    // User wants to identify and name the file
                    var identified = await IdentifyAndProcessFileAsync(filePath, fileName);
                    if (identified)
                    {
                        processedCount++;
                    }
                    else
                    {
                        skippedCount++;
                    }
                }
                else
                {
                    // User doesn't want to name it, ask if they want to move it anyway
                    var moveAnywayResult = _promptService.ConfirmPrompt(new ConfirmPromptOptions
                    {
                        Question = "Move to file server with original name?",
                        DefaultValue = false
                    });

                    if (!moveAnywayResult.Success || moveAnywayResult.Cancelled)
                    {
                        skippedCount++;
                        continue;
                    }

                    if (moveAnywayResult.Value)
                    {
                        await MoveFileToServerAsync(filePath, fileName);
                        processedCount++;
                    }
                    else
                    {
                        _logger.LogInformation($"Skipping file: {fileName}");
                        skippedCount++;
                    }
                }
            }

            // Summary
            Console.WriteLine();
            _promptService.DisplayHeader("SUMMARY");
            Console.WriteLine($"Total files found: {mkvFiles.Count}");
            Console.WriteLine($"Files processed: {processedCount}");
            Console.WriteLine($"Files skipped: {skippedCount}");
            Console.WriteLine();
            _logger.LogInformation($"Discover and Name workflow completed. Processed: {processedCount}, Skipped: {skippedCount}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Discover and Name workflow");
            _consoleOutput.ShowError($"An error occurred: {ex.Message}");
        }
    }

    private async Task<bool> IdentifyAndProcessFileAsync(string filePath, string fileName)
    {
        try
        {
            // Search for media using interactive search
            var searchTitle = Path.GetFileNameWithoutExtension(fileName);
            var searchResult = await _mediaSelectionService.InteractiveMediaSearchAsync(searchTitle, fileName);

            if (searchResult == null)
            {
                _logger.LogInformation($"User skipped identification for: {fileName}");
                return false;
            }

            var mediaIdentity = ModelConverter.ToMediaIdentity(searchResult);

            // Determine if it's a movie or TV series
            bool isSeries = mediaIdentity.Type?.Equals("series", StringComparison.OrdinalIgnoreCase) == true;

            if (isSeries)
            {
                return await ProcessTvSeriesFileAsync(filePath, fileName, mediaIdentity);
            }
            else
            {
                return await ProcessMovieFileAsync(filePath, fileName, mediaIdentity);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error identifying and processing file: {fileName}");
            _consoleOutput.ShowError($"Error processing file: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> ProcessMovieFileAsync(string filePath, string fileName, MediaIdentity mediaIdentity)
    {
        try
        {
            var movieInfo = MovieInfo.FromMediaIdentity(mediaIdentity);

            // Generate new filename
            var newFileName = _namingService.GenerateMovieFileName(
                movieInfo.Title!,
                movieInfo.Year,
                Path.GetExtension(filePath));

            // Get target directory
            var movieDir = _namingService.GetMovieDirectory(_ripSettings.Output, movieInfo.Title!, movieInfo.Year);
            var newFilePath = Path.Combine(movieDir, newFileName);

            // Ensure directory exists
            FileSystemHelper.EnsureDirectoryExists(movieDir);

            // Move the file
            if (File.Exists(newFilePath))
            {
                var overwriteResult = _promptService.ConfirmPrompt(new ConfirmPromptOptions
                {
                    Question = $"File already exists at destination. Overwrite?",
                    DefaultValue = false
                });

                if (!overwriteResult.Success || !overwriteResult.Value)
                {
                    _logger.LogInformation($"User chose not to overwrite existing file: {newFilePath}");
                    return false;
                }

                File.Delete(newFilePath);
            }

            File.Move(filePath, newFilePath);

            _consoleOutput.ShowFileRenamed(fileName, newFileName);
            _consoleOutput.ShowFilesMovedTo(movieDir);
            _logger.LogInformation($"Renamed and moved: {fileName} -> {newFileName}");

            // Trigger file transfer if enabled
            await _moverService.FindFiles(_ripSettings.Output);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error processing movie file: {fileName}");
            _consoleOutput.ShowError($"Error processing movie: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> ProcessTvSeriesFileAsync(string filePath, string fileName, MediaIdentity mediaIdentity)
    {
        try
        {
            var seriesInfo = SeriesInfo.FromMediaIdentity(mediaIdentity);

            // Prompt for season and episode numbers
            var seasonResult = _promptService.NumberPrompt(new NumberPromptOptions
            {
                Question = "Enter season number:",
                Required = true,
                PromptText = "Season",
                MinValue = 1,
                MaxValue = 50
            });

            if (!seasonResult.Success || seasonResult.Cancelled)
            {
                return false;
            }

            var episodeResult = _promptService.NumberPrompt(new NumberPromptOptions
            {
                Question = "Enter episode number:",
                Required = true,
                PromptText = "Episode",
                MinValue = 1,
                MaxValue = 999
            });

            if (!episodeResult.Success || episodeResult.Cancelled)
            {
                return false;
            }

            int season = seasonResult.Value;
            int episode = episodeResult.Value;

            // Get episode info from OMDB
            var episodeInfo = await _omdbService.GetEpisodeInfoAsync(seriesInfo.Title!, season, episode);

            // Generate new filename
            var newFileName = _namingService.GenerateEpisodeFileName(
                seriesInfo.Title!,
                season,
                episode,
                episodeInfo?.Title,
                Path.GetExtension(filePath));

            // Get target directory
            var seriesDir = _namingService.GetSeriesDirectory(_ripSettings.Output, seriesInfo.Title!);
            var seasonDir = Path.Combine(seriesDir, _namingService.GetSeasonDirectory(season));
            var newFilePath = Path.Combine(seasonDir, newFileName);

            // Ensure directory exists
            FileSystemHelper.EnsureDirectoryExists(seasonDir);

            // Move the file
            if (File.Exists(newFilePath))
            {
                var overwriteResult = _promptService.ConfirmPrompt(new ConfirmPromptOptions
                {
                    Question = $"File already exists at destination. Overwrite?",
                    DefaultValue = false
                });

                if (!overwriteResult.Success || !overwriteResult.Value)
                {
                    _logger.LogInformation($"User chose not to overwrite existing file: {newFilePath}");
                    return false;
                }

                File.Delete(newFilePath);
            }

            File.Move(filePath, newFilePath);

            _consoleOutput.ShowFileRenamed(fileName, newFileName);
            _consoleOutput.ShowFilesMovedTo(seasonDir);
            _logger.LogInformation($"Renamed and moved: {fileName} -> {newFileName} (S{season:D2}E{episode:D2})");

            // Trigger file transfer if enabled
            await _moverService.FindFiles(_ripSettings.Output);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error processing TV series file: {fileName}");
            _consoleOutput.ShowError($"Error processing TV series: {ex.Message}");
            return false;
        }
    }

    private async Task MoveFileToServerAsync(string filePath, string fileName)
    {
        try
        {
            if (!_fileTransferSettings.Enabled)
            {
                _consoleOutput.ShowWarning("File transfer is not enabled in settings.");
                _logger.LogWarning("File transfer requested but not enabled");
                return;
            }

            // Check if the file is already in a subdirectory (already organized)
            // If so, transfer it directly without moving to _unidentified
            var fileDirectory = Path.GetDirectoryName(filePath) ?? string.Empty;
            var outputPath = Path.GetFullPath(_ripSettings.Output);
            var isInSubdirectory = !Path.GetFullPath(fileDirectory).Equals(outputPath, StringComparison.OrdinalIgnoreCase);

            string fileToTransfer;
            string relativePath;

            if (isInSubdirectory)
            {
                // File is already organized (in a subdirectory), transfer it directly
                fileToTransfer = filePath;
                relativePath = Path.GetRelativePath(outputPath, filePath);
                _logger.LogInformation("File is already organized, transferring directly: {RelativePath}", relativePath);
                Console.WriteLine($"File is already organized, transferring: {relativePath}");
            }
            else
            {
                // File is in root output folder, move to _unidentified first
                var unidentifiedDir = Path.Combine(_ripSettings.Output, "_unidentified");
                FileSystemHelper.EnsureDirectoryExists(unidentifiedDir);

                var destinationPath = Path.Combine(unidentifiedDir, fileName);

                // Move file to unidentified folder first
                File.Move(filePath, destinationPath);

                fileToTransfer = destinationPath;
                relativePath = Path.GetRelativePath(outputPath, destinationPath);

                _logger.LogInformation("Moved to unidentified folder: {FileName}", fileName);
                _consoleOutput.ShowFilesMovedTo(unidentifiedDir);
            }

            // Transfer the file directly with progress shown
            Console.WriteLine($"Starting transfer: {relativePath}");
            var result = await _fileTransferClient.SendFileInBackground(relativePath, fileToTransfer);

            if (result == true)
            {
                _logger.LogInformation("Successfully transferred file: {RelativePath}", relativePath);
                Console.WriteLine($"Successfully transferred: {relativePath}");
            }
            else if (result == false)
            {
                _logger.LogWarning("Failed to transfer file: {RelativePath}", relativePath);
                _consoleOutput.ShowError($"Failed to transfer: {relativePath}");
            }
            else
            {
                _logger.LogDebug("File transfer skipped (disabled): {RelativePath}", relativePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error moving file to server: {FileName}", fileName);
            _consoleOutput.ShowError($"Error moving file: {ex.Message}");
        }
    }
}
