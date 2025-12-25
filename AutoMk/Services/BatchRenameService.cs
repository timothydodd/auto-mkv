using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutoMk.Interfaces;
using AutoMk.Models;
using AutoMk.Utilities;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace AutoMk.Services;

/// <summary>
/// Service for collecting, confirming, and executing batch file renames.
/// Provides a workflow where users can review all proposed renames before execution.
/// </summary>
public class BatchRenameService : IBatchRenameService
{
    private readonly ILogger<BatchRenameService> _logger;
    private readonly IMediaNamingService _namingService;
    private readonly IFileDiscoveryService _fileDiscoveryService;
    private readonly IEnhancedOmdbService _omdbService;
    private readonly IMediaSelectionService _mediaSelectionService;
    private readonly IConsolePromptService _promptService;
    private readonly IConsoleOutputService _consoleOutput;
    private readonly ConsoleInteractionService _consoleInteraction;
    private readonly RipSettings _ripSettings;

    public BatchRenameService(
        ILogger<BatchRenameService> logger,
        IMediaNamingService namingService,
        IFileDiscoveryService fileDiscoveryService,
        IEnhancedOmdbService omdbService,
        IMediaSelectionService mediaSelectionService,
        IConsolePromptService promptService,
        IConsoleOutputService consoleOutput,
        ConsoleInteractionService consoleInteraction,
        RipSettings ripSettings)
    {
        _logger = ValidationHelper.ValidateNotNull(logger);
        _namingService = ValidationHelper.ValidateNotNull(namingService);
        _fileDiscoveryService = ValidationHelper.ValidateNotNull(fileDiscoveryService);
        _omdbService = ValidationHelper.ValidateNotNull(omdbService);
        _mediaSelectionService = ValidationHelper.ValidateNotNull(mediaSelectionService);
        _promptService = ValidationHelper.ValidateNotNull(promptService);
        _consoleOutput = ValidationHelper.ValidateNotNull(consoleOutput);
        _consoleInteraction = ValidationHelper.ValidateNotNull(consoleInteraction);
        _ripSettings = ValidationHelper.ValidateNotNull(ripSettings);
    }

    public async Task<List<PendingRename>> CollectTvSeriesRenamesAsync(
        string outputPath,
        string discName,
        List<AkTitle> tracks,
        SeriesInfo seriesInfo,
        DiscInfo discInfo,
        SeriesState seriesState)
    {
        var pendingRenames = new List<PendingRename>();
        _logger.LogInformation($"Collecting renames for {tracks.Count} TV series tracks");

        for (int i = 0; i < tracks.Count; i++)
        {
            var track = tracks[i];

            // Get episode number from mapping
            var episodeNumbers = discInfo.TrackToEpisodeMapping.ContainsKey(i)
                ? discInfo.TrackToEpisodeMapping[i]
                : new List<int> { discInfo.StartingEpisode + i };

            var episodeNumber = episodeNumbers.FirstOrDefault();

            // Find the ripped file
            var originalFile = _fileDiscoveryService.FindRippedFile(outputPath, track, discName);
            if (originalFile == null)
            {
                _logger.LogWarning($"Could not find ripped file for track: {track.Id}");
                continue;
            }

            // Get episode info from OMDB
            var episodeInfo = await _omdbService.GetEpisodeInfoAsync(
                seriesInfo.Title!,
                discInfo.Season,
                episodeNumber);

            // Generate new filename
            var newFileName = _namingService.GenerateEpisodeFileName(
                seriesInfo.Title!,
                discInfo.Season,
                episodeNumber,
                episodeInfo?.Title,
                Path.GetExtension(originalFile));

            var newFilePath = Path.Combine(
                _namingService.GetSeriesDirectory(outputPath, seriesInfo.Title!),
                _namingService.GetSeasonDirectory(discInfo.Season),
                newFileName);

            var fileInfo = new FileInfo(originalFile);

            pendingRenames.Add(new PendingRename
            {
                OriginalFilePath = originalFile,
                OriginalFileName = Path.GetFileName(originalFile),
                ProposedFilePath = newFilePath,
                ProposedFileName = newFileName,
                Track = track,
                SizeInGB = fileInfo.Length / (1024.0 * 1024.0 * 1024.0),
                Season = discInfo.Season,
                EpisodeNumber = episodeNumber,
                EpisodeTitle = episodeInfo?.Title,
                SeriesInfo = seriesInfo,
                TrackIndex = i
            });
        }

        _logger.LogInformation($"Collected {pendingRenames.Count} pending renames");
        return pendingRenames;
    }

    public async Task<List<PendingRename>> CollectMovieRenamesAsync(
        string outputPath,
        string discName,
        Dictionary<AkTitle, MovieInfo> trackMovieMapping)
    {
        var pendingRenames = new List<PendingRename>();
        _logger.LogInformation($"Collecting renames for {trackMovieMapping.Count} movie tracks");

        int index = 0;
        foreach (var (track, movieInfo) in trackMovieMapping)
        {
            var rename = await CollectSingleMovieRenameAsync(outputPath, track, movieInfo, discName);
            if (rename != null)
            {
                rename.TrackIndex = index++;
                pendingRenames.Add(rename);
            }
        }

        _logger.LogInformation($"Collected {pendingRenames.Count} pending movie renames");
        return pendingRenames;
    }

    public async Task<PendingRename?> CollectSingleMovieRenameAsync(
        string outputPath,
        AkTitle track,
        MovieInfo movieInfo,
        string? discName = null)
    {
        // Find the ripped file
        var originalFile = _fileDiscoveryService.FindRippedFile(outputPath, track, discName ?? "");
        if (originalFile == null)
        {
            _logger.LogWarning($"Could not find ripped file for track: {track.Id}");
            return null;
        }

        // Generate new filename
        var newFileName = _namingService.GenerateMovieFileName(
            movieInfo.Title!,
            movieInfo.Year,
            Path.GetExtension(originalFile));

        var movieDir = _namingService.GetMovieDirectory(outputPath, movieInfo.Title!, movieInfo.Year);
        var newFilePath = Path.Combine(movieDir, newFileName);

        var fileInfo = new FileInfo(originalFile);

        return new PendingRename
        {
            OriginalFilePath = originalFile,
            OriginalFileName = Path.GetFileName(originalFile),
            ProposedFilePath = newFilePath,
            ProposedFileName = newFileName,
            Track = track,
            SizeInGB = fileInfo.Length / (1024.0 * 1024.0 * 1024.0),
            MovieInfo = movieInfo
        };
    }

    public async Task<List<PendingRename>> CollectManualTvSeriesRenamesAsync(
        string outputPath,
        string discName,
        SeriesInfo seriesInfo,
        Dictionary<AkTitle, EpisodeMapping> episodeMapping)
    {
        var pendingRenames = new List<PendingRename>();
        _logger.LogInformation($"Collecting renames for {episodeMapping.Count} manually mapped TV series tracks");

        int index = 0;
        foreach (var (track, episodeInfo) in episodeMapping)
        {
            // Find the ripped file
            var originalFile = _fileDiscoveryService.FindRippedFile(outputPath, track, discName);
            if (originalFile == null)
            {
                _logger.LogWarning($"Could not find ripped file for track: {track.Id}");
                continue;
            }

            // Get episode info from OMDB
            CachedEpisodeInfo? cachedEpisodeInfo = null;
            try
            {
                cachedEpisodeInfo = await _omdbService.GetEpisodeInfoAsync(
                    seriesInfo.Title!,
                    episodeInfo.Season,
                    episodeInfo.Episode);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Could not fetch episode details from OMDB for S{episodeInfo.Season:D2}E{episodeInfo.Episode:D2}");
            }

            // Generate new filename
            var newFileName = _namingService.GenerateEpisodeFileName(
                seriesInfo.Title!,
                episodeInfo.Season,
                episodeInfo.Episode,
                cachedEpisodeInfo?.Title,
                Path.GetExtension(originalFile));

            var newFilePath = Path.Combine(
                _namingService.GetSeriesDirectory(outputPath, seriesInfo.Title!),
                _namingService.GetSeasonDirectory(episodeInfo.Season),
                newFileName);

            var fileInfo = new FileInfo(originalFile);

            pendingRenames.Add(new PendingRename
            {
                OriginalFilePath = originalFile,
                OriginalFileName = Path.GetFileName(originalFile),
                ProposedFilePath = newFilePath,
                ProposedFileName = newFileName,
                Track = track,
                SizeInGB = fileInfo.Length / (1024.0 * 1024.0 * 1024.0),
                Season = episodeInfo.Season,
                EpisodeNumber = episodeInfo.Episode,
                EpisodeTitle = cachedEpisodeInfo?.Title,
                SeriesInfo = seriesInfo,
                TrackIndex = index++
            });
        }

        _logger.LogInformation($"Collected {pendingRenames.Count} pending manual TV series renames");
        return pendingRenames;
    }

    public async Task<BatchRenameResult> ConfirmAndProcessRenamesAsync(
        List<PendingRename> pendingRenames,
        BatchRenameOptions? options = null)
    {
        options ??= new BatchRenameOptions();

        if (pendingRenames.Count == 0)
        {
            _logger.LogWarning("No pending renames to confirm");
            return BatchRenameResult.Cancelled();
        }

        // Skip confirmation for single track if configured
        if (pendingRenames.Count == 1 && options.SkipConfirmationForSingleTrack)
        {
            _logger.LogInformation("Single track - skipping confirmation");
            return BatchRenameResult.Success(pendingRenames);
        }

        // Main confirmation loop
        while (true)
        {
            // Display the rename table
            DisplayRenameTable(pendingRenames);

            // Build choices
            var choices = new List<PromptChoice>
            {
                new("confirm", "Confirm and rename all files", description: $"Rename {pendingRenames.Count} files")
            };

            if (options.AllowRelookup)
            {
                choices.Add(new PromptChoice("relookup", "Re-lookup a specific track", description: "Search OMDB again for a track"));
            }

            choices.Add(new PromptChoice("cancel", "Cancel", description: "Keep original filenames"));

            var result = _promptService.SelectPrompt(new SelectPromptOptions
            {
                Question = "What would you like to do?",
                Choices = choices,
                AllowCancel = false
            });

            if (!result.Success)
            {
                return BatchRenameResult.Cancelled();
            }

            switch (result.Value)
            {
                case "confirm":
                    _logger.LogInformation("User confirmed batch rename");
                    return BatchRenameResult.Success(pendingRenames);

                case "relookup":
                    var trackToRelookup = SelectTrackToRelookup(pendingRenames);
                    if (trackToRelookup != null)
                    {
                        var updatedRename = await RelookupTrackAsync(trackToRelookup);
                        var index = pendingRenames.FindIndex(r => r.TrackIndex == trackToRelookup.TrackIndex);
                        if (index >= 0)
                        {
                            pendingRenames[index] = updatedRename;
                        }
                    }
                    // Loop continues to show updated table
                    break;

                case "cancel":
                    _logger.LogInformation("User cancelled batch rename");
                    return BatchRenameResult.Cancelled();
            }
        }
    }

    public async Task<bool> ExecuteRenamesAsync(List<PendingRename> confirmedRenames)
    {
        if (confirmedRenames.Count == 0)
        {
            _logger.LogWarning("No renames to execute");
            return true;
        }

        _logger.LogInformation($"Executing {confirmedRenames.Count} file renames");
        bool allSuccessful = true;

        foreach (var rename in confirmedRenames)
        {
            try
            {
                // Ensure destination directory exists
                var destDir = Path.GetDirectoryName(rename.ProposedFilePath);
                if (!string.IsNullOrEmpty(destDir))
                {
                    FileSystemHelper.EnsureDirectoryExists(destDir);
                }

                // Try to move the file with retry logic
                bool moveSuccessful = await MoveFileWithRetryAsync(rename);

                if (moveSuccessful)
                {
                    _consoleOutput.ShowFileRenamed(rename.OriginalFileName, rename.ProposedFileName);
                    _consoleOutput.ShowFilesMovedTo(destDir!);
                    _logger.LogInformation($"Renamed: {rename.OriginalFileName} -> {rename.ProposedFileName}");
                }
                else
                {
                    allSuccessful = false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error renaming file: {rename.OriginalFileName}");
                allSuccessful = false;
            }
        }

        return allSuccessful;
    }

    public async Task<PendingRename> RelookupTrackAsync(PendingRename pendingRename)
    {
        _promptService.DisplayHeader($"Re-lookup: {pendingRename.OriginalFileName}");

        if (pendingRename.IsTvSeries)
        {
            return await RelookupTvEpisodeAsync(pendingRename);
        }
        else if (pendingRename.IsMovie)
        {
            return await RelookupMovieAsync(pendingRename);
        }

        return pendingRename;
    }

    private async Task<PendingRename> RelookupTvEpisodeAsync(PendingRename pendingRename)
    {
        AnsiConsole.MarkupLine($"[dim]Current:[/] [yellow]S{pendingRename.Season:D2}E{pendingRename.EpisodeNumber:D2}[/] - [white]{Markup.Escape(pendingRename.EpisodeTitle ?? "Unknown")}[/]");
        AnsiConsole.WriteLine();

        // Ask for new season/episode
        var seasonResult = _promptService.NumberPrompt(new NumberPromptOptions
        {
            Question = "Enter season number:",
            Required = true,
            PromptText = "Season",
            MinValue = 1,
            MaxValue = 50,
            DefaultValue = pendingRename.Season
        });

        if (!seasonResult.Success || seasonResult.Cancelled)
        {
            return pendingRename;
        }

        var episodeResult = _promptService.NumberPrompt(new NumberPromptOptions
        {
            Question = "Enter episode number:",
            Required = true,
            PromptText = "Episode",
            MinValue = 1,
            MaxValue = 999,
            DefaultValue = pendingRename.EpisodeNumber
        });

        if (!episodeResult.Success || episodeResult.Cancelled)
        {
            return pendingRename;
        }

        var newSeason = seasonResult.Value;
        var newEpisode = episodeResult.Value;

        // Look up new episode info
        var episodeInfo = await _omdbService.GetEpisodeInfoAsync(
            pendingRename.SeriesInfo!.Title!,
            newSeason,
            newEpisode);

        // Generate new filename
        var newFileName = _namingService.GenerateEpisodeFileName(
            pendingRename.SeriesInfo!.Title!,
            newSeason,
            newEpisode,
            episodeInfo?.Title,
            Path.GetExtension(pendingRename.OriginalFilePath));

        var newFilePath = Path.Combine(
            _namingService.GetSeriesDirectory(_ripSettings.Output, pendingRename.SeriesInfo.Title!),
            _namingService.GetSeasonDirectory(newSeason),
            newFileName);

        // Update the pending rename
        pendingRename.Season = newSeason;
        pendingRename.EpisodeNumber = newEpisode;
        pendingRename.EpisodeTitle = episodeInfo?.Title;
        pendingRename.ProposedFileName = newFileName;
        pendingRename.ProposedFilePath = newFilePath;

        AnsiConsole.MarkupLine($"[green]Updated to:[/] [yellow]S{newSeason:D2}E{newEpisode:D2}[/] - [white]{Markup.Escape(episodeInfo?.Title ?? "Unknown")}[/]");
        AnsiConsole.WriteLine();

        return pendingRename;
    }

    private async Task<PendingRename> RelookupMovieAsync(PendingRename pendingRename)
    {
        AnsiConsole.MarkupLine($"[dim]Current:[/] [white]{Markup.Escape(pendingRename.MovieInfo?.Title ?? "Unknown")}[/] [dim]({pendingRename.MovieInfo?.Year})[/]");
        AnsiConsole.WriteLine();

        // Use interactive search
        var searchTitle = pendingRename.MovieInfo?.Title ?? Path.GetFileNameWithoutExtension(pendingRename.OriginalFileName);
        var result = await _mediaSelectionService.InteractiveMediaSearchAsync(searchTitle, pendingRename.OriginalFileName);

        if (result == null)
        {
            return pendingRename;
        }

        // Update movie info
        var newMovieInfo = new MovieInfo
        {
            Title = result.Title,
            Year = result.Year,
            Type = result.Type,
            ImdbID = result.ImdbID
        };

        // Generate new filename
        var newFileName = _namingService.GenerateMovieFileName(
            newMovieInfo.Title!,
            newMovieInfo.Year,
            Path.GetExtension(pendingRename.OriginalFilePath));

        var movieDir = _namingService.GetMovieDirectory(_ripSettings.Output, newMovieInfo.Title!, newMovieInfo.Year);
        var newFilePath = Path.Combine(movieDir, newFileName);

        pendingRename.MovieInfo = newMovieInfo;
        pendingRename.ProposedFileName = newFileName;
        pendingRename.ProposedFilePath = newFilePath;

        AnsiConsole.MarkupLine($"[green]Updated to:[/] [white]{Markup.Escape(newMovieInfo.Title ?? "")}[/] [dim]({newMovieInfo.Year})[/]");
        AnsiConsole.WriteLine();

        return pendingRename;
    }

    private void DisplayRenameTable(List<PendingRename> pendingRenames)
    {
        AnsiConsole.WriteLine();
        _promptService.DisplayHeader($"PROPOSED FILE RENAMES ({pendingRenames.Count} files)");

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn(new TableColumn("[white]#[/]").Centered())
            .AddColumn(new TableColumn("[white]Original File[/]"))
            .AddColumn(new TableColumn("[white]New Name[/]"))
            .AddColumn(new TableColumn("[white]Size[/]").RightAligned());

        for (int i = 0; i < pendingRenames.Count; i++)
        {
            var rename = pendingRenames[i];
            var typeInfo = rename.IsTvSeries
                ? $"[cyan]S{rename.Season:D2}E{rename.EpisodeNumber:D2}[/]"
                : $"[yellow]{rename.MovieInfo?.Year}[/]";

            table.AddRow(
                $"[cyan]{i + 1}[/]",
                Markup.Escape(TruncateString(rename.OriginalFileName, 30)),
                $"{typeInfo} {Markup.Escape(TruncateString(rename.ProposedFileName, 35))}",
                $"[dim]{rename.SizeInGB:F2} GB[/]"
            );
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private PendingRename? SelectTrackToRelookup(List<PendingRename> pendingRenames)
    {
        var choices = pendingRenames.Select((r, i) => new PromptChoice(
            i.ToString(),
            $"{i + 1}. {TruncateString(r.OriginalFileName, 25)} -> {TruncateString(r.ProposedFileName, 30)}",
            r)).ToList();

        choices.Add(new PromptChoice("back", "Back to confirmation"));

        var result = _promptService.SelectPrompt<PendingRename?>(new SelectPromptOptions
        {
            HeaderText = "Select Track to Re-lookup",
            Question = "Which track would you like to re-lookup?",
            Choices = choices,
            AllowCancel = true
        });

        if (!result.Success || result.Cancelled || result.Value == null)
        {
            return null;
        }

        return result.Value;
    }

    private async Task<bool> MoveFileWithRetryAsync(PendingRename rename)
    {
        const int maxRetries = 3;
        int retryCount = 0;

        while (retryCount < maxRetries)
        {
            try
            {
                File.Move(rename.OriginalFilePath, rename.ProposedFilePath);
                return true;
            }
            catch (IOException ioEx) when (ioEx.Message.Contains("being used by another process") ||
                                          ioEx.Message.Contains("Access to the path") ||
                                          ioEx.Message.Contains("denied"))
            {
                retryCount++;

                if (retryCount >= maxRetries)
                {
                    var userChoice = _consoleInteraction.HandleFileAccessError(
                        rename.IsTvSeries ? rename.SeriesInfo?.Title ?? "Unknown" : rename.MovieInfo?.Title ?? "Unknown",
                        rename.Season ?? 0,
                        rename.EpisodeNumber ?? 1,
                        rename.OriginalFileName,
                        ioEx.Message);

                    switch (userChoice)
                    {
                        case ConsoleInteractionService.FileAccessErrorChoice.Retry:
                            retryCount = 0;
                            continue;

                        case ConsoleInteractionService.FileAccessErrorChoice.Skip:
                            _logger.LogWarning($"User chose to skip file: {rename.OriginalFileName}");
                            return false;

                        case ConsoleInteractionService.FileAccessErrorChoice.Exit:
                            _logger.LogInformation("User chose to exit application");
                            Environment.Exit(0);
                            break;
                    }
                }
                else
                {
                    _logger.LogWarning($"File access error (attempt {retryCount}/{maxRetries}): {ioEx.Message}");
                    _logger.LogInformation("Waiting 2 seconds before retry...");
                    await Task.Delay(2000);
                }
            }
        }

        return false;
    }

    private static string TruncateString(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value ?? "";
        }

        return value[..(maxLength - 3)] + "...";
    }
}
