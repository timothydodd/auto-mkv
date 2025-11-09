using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AutoMk.Extensions;
using AutoMk.Interfaces;
using AutoMk.Models;
using AutoMk.Utilities;
using Microsoft.Extensions.Logging;

namespace AutoMk.Services;

public class MediaIdentificationService : IMediaIdentificationService
{
    private readonly IOmdbClient _omdbClient;
    private readonly ILogger<MediaIdentificationService> _logger;
    private readonly IMediaNamingService _namingService;
    private readonly IMediaStateManager _stateManager;
    private readonly ConsoleInteractionService _consoleInteraction;
    private readonly IEnhancedOmdbService _enhancedOmdbService;
    private readonly IFileDiscoveryService _fileDiscoveryService;
    private readonly IMediaSelectionService _mediaSelectionService;
    private readonly ISeriesConfigurationService _seriesConfigurationService;
    private readonly IPatternLearningService _patternLearningService;
    private readonly IConsoleOutputService _consoleOutput;

    public MediaIdentificationService(
        IOmdbClient omdbClient,
        ILogger<MediaIdentificationService> logger,
        IMediaNamingService namingService,
        IMediaStateManager stateManager,
        ConsoleInteractionService consoleInteraction,
        IEnhancedOmdbService enhancedOmdbService,
        IFileDiscoveryService fileDiscoveryService,
        IMediaSelectionService mediaSelectionService,
        ISeriesConfigurationService seriesConfigurationService,
        IPatternLearningService patternLearningService,
        IConsoleOutputService consoleOutput)
    {
        _omdbClient = ValidationHelper.ValidateNotNull(omdbClient);
        _logger = ValidationHelper.ValidateNotNull(logger);
        _namingService = ValidationHelper.ValidateNotNull(namingService);
        _stateManager = ValidationHelper.ValidateNotNull(stateManager);
        _consoleInteraction = ValidationHelper.ValidateNotNull(consoleInteraction);
        _enhancedOmdbService = ValidationHelper.ValidateNotNull(enhancedOmdbService);
        _fileDiscoveryService = ValidationHelper.ValidateNotNull(fileDiscoveryService);
        _mediaSelectionService = ValidationHelper.ValidateNotNull(mediaSelectionService);
        _seriesConfigurationService = ValidationHelper.ValidateNotNull(seriesConfigurationService);
        _patternLearningService = ValidationHelper.ValidateNotNull(patternLearningService);
        _consoleOutput = ValidationHelper.ValidateNotNull(consoleOutput);
    }

    public async Task<bool> ProcessRippedMediaAsync(string outputPath, string discName, List<AkTitle> rippedTracks)
    {
        return await ProcessRippedMediaAsync(outputPath, discName, rippedTracks, null);
    }

    public async Task<bool> ProcessRippedMediaAsync(string outputPath, string discName, List<AkTitle> rippedTracks, PreIdentifiedMedia? preIdentifiedMedia)
    {
        _logger.LogInformation($"Processing ripped media from disc: {discName}");

        try
        {
            MediaIdentity? mediaInfo;

            // Use pre-identified media if available
            if (preIdentifiedMedia?.MediaData != null)
            {
                mediaInfo = preIdentifiedMedia.MediaData;
                _logger.LogInformation($"Using pre-identified media: {mediaInfo.Title} ({mediaInfo.Type})");
            }
            else
            {
                // First, try to identify what type of media this is
                mediaInfo = await IdentifyMediaAsync(discName);
                if (mediaInfo == null)
                {
                    _logger.LogWarning($"Could not identify media type for disc: {discName}");

                    // Try interactive search
                    var searchTitle = ParseDiscName(discName).SeriesName ?? ExtractSeriesNameForSearch(discName);
                    var searchResult = await _mediaSelectionService.InteractiveMediaSearchAsync(searchTitle, discName);

                    if (searchResult == null)
                    {
                        _logger.LogInformation("User skipped disc identification. Files will remain with original names.");
                        return false;
                    }

                    mediaInfo = ModelConverter.ToMediaIdentity(searchResult);
                    // Save the manual identification for future use
                    await _stateManager.SaveManualIdentificationAsync(discName, mediaInfo);
                    _logger.LogInformation($"Saved manual identification for future discs matching pattern.");
                }
            }

            // Process based on media type
            if (mediaInfo.Type?.Equals("series", StringComparison.OrdinalIgnoreCase) == true)
            {
                var seriesInfo = SeriesInfo.FromMediaIdentity(mediaInfo);
                return await ProcessTvSeriesAsync(outputPath, discName, rippedTracks, seriesInfo);
            }
            else if (mediaInfo.Type?.Equals("movie", StringComparison.OrdinalIgnoreCase) == true)
            {
                var movieInfo = MovieInfo.FromMediaIdentity(mediaInfo);
                return await ProcessMovie(outputPath, discName, rippedTracks, movieInfo);
            }
            else
            {
                // Try both movie and series search
                return await ProcessUnknownMediaAsync(outputPath, discName, rippedTracks);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error processing ripped media for disc: {discName}");
            return false;
        }
    }

    public async Task<PreIdentifiedMedia?> PreIdentifyMediaAsync(string discName, List<AkTitle> titlesToRip, bool isAutoMode = true)
    {
        // Reset disc-specific auto-accept flag for the new disc
        _seriesConfigurationService.ResetDiscAutoAccept();

        _logger.LogInformation($"Pre-identifying media for disc: {discName} (Auto Mode: {isAutoMode})");

        try
        {
            // Check manual identification cache first
            var cachedIdentification = await _stateManager.GetManualIdentificationAsync(discName);
            if (cachedIdentification != null)
            {
                _logger.LogInformation($"Found cached manual identification for '{discName}': {cachedIdentification.MediaTitle}");
                var mediaIdentity = cachedIdentification.CachedOmdbData != null ? ModelConverter.ToMediaIdentity(cachedIdentification.CachedOmdbData) : null;
                return CreatePreIdentifiedMedia(mediaIdentity, true, "manual");
            }

            // Parse disc name and search
            var parsedInfo = ParseDiscName(discName);
            var searchTitle = !string.IsNullOrEmpty(parsedInfo.SeriesName) ? parsedInfo.SeriesName : ExtractSeriesNameForSearch(discName);

            _logger.LogInformation($"Searching for media: '{searchTitle}' (from disc: '{discName}')");

            // Search for media in OMDB
            var (seriesResult, movieResult) = await SearchForMediaInOmdbAsync(searchTitle);

            // Check if we found both
            bool foundSeries = IsValidConfirmationInfo(seriesResult);
            bool foundMovie = IsValidConfirmationInfo(movieResult);

            if (foundSeries && foundMovie)
            {
                return await HandleBothMediaTypesFoundAsync(discName, movieResult, seriesResult);
            }
            else if (foundSeries)
            {
                return await HandleSeriesFoundAsync(discName, searchTitle, seriesResult);
            }
            else if (foundMovie)
            {
                return await HandleMovieFoundAsync(discName, searchTitle, movieResult, isAutoMode);
            }

            // Try searching with year extraction
            var movieWithYearResult = await TrySearchMovieWithYearAsync(discName, searchTitle, isAutoMode);
            if (movieWithYearResult != null)
            {
                return movieWithYearResult;
            }

            // Could not identify automatically - prompt user
            return await HandleNoAutomaticMatchAsync(discName, searchTitle);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error pre-identifying media for disc: {discName}");
            return null;
        }
    }

    private async Task<MediaIdentity?> IdentifyMediaAsync(string discName)
    {
        // Check manual identification cache first
        var cachedIdentification = await _stateManager.GetManualIdentificationAsync(discName);
        if (cachedIdentification != null && cachedIdentification.ImdbId != null)
        {
            _logger.LogInformation($"Found cached manual identification for '{discName}': {cachedIdentification.MediaTitle}");
            return cachedIdentification.CachedOmdbData != null ? ModelConverter.ToMediaIdentity(cachedIdentification.CachedOmdbData) : null;
        }

        // First try to parse as TV series to extract clean series name
        var parsedInfo = ParseDiscName(discName);
        var searchTitle = !string.IsNullOrEmpty(parsedInfo.SeriesName) ? parsedInfo.SeriesName : ExtractSeriesNameForSearch(discName);

        _logger.LogInformation($"Searching for media: '{searchTitle}' (from disc: '{discName}')");

        // Try series first
        var seriesResult = await _omdbClient.GetSeries(searchTitle);
        if (seriesResult.IsValidOmdbResponse())
        {
            _logger.LogInformation($"Identified as TV Series: {seriesResult.Title}");
            return ModelConverter.ToMediaIdentity(seriesResult);
        }

        // Try movie search
        var movieResult = await _omdbClient.GetMovie(searchTitle, null);
        if (movieResult.IsValidOmdbResponse())
        {
            _logger.LogInformation($"Identified as Movie: {movieResult.Title}");
            return ModelConverter.ToMediaIdentity(movieResult);
        }

        // Try searching with year extraction
        var (title, year) = ExtractTitleAndYear(searchTitle);
        if (year.HasValue)
        {
            var movieWithYear = await _omdbClient.GetMovie(title, year);
            if (movieWithYear.IsValidOmdbResponse())
            {
                _logger.LogInformation($"Identified as Movie with year: {movieWithYear.Title} ({year})");
                return ModelConverter.ToMediaIdentity(movieWithYear);
            }
        }

        return null;
    }

    private async Task<bool> ProcessTvSeriesAsync(string outputPath, string discName, List<AkTitle> rippedTracks, SeriesInfo seriesInfo)
    {
        _logger.LogInformation($"Processing TV Series: {seriesInfo.Title}");

        var parsedDiscInfo = ParseDiscName(discName);
        _logger.LogInformation($"Parsed disc info - Season: {parsedDiscInfo.Season}, Disc: {parsedDiscInfo.DiscNumber}");

        var seriesState = await _stateManager.GetOrCreateSeriesStateAsync(seriesInfo.Title!);

        await HandleSeasonMismatchAsync(seriesState, parsedDiscInfo, seriesInfo.Title!, discName);

        var (useAutoIncrement, isReprocessing) = await ConfigureAutoIncrementModeAsync(seriesState, seriesInfo.Title!, discName);
        await HandleSeasonEpisodePromptsAsync(seriesState, seriesInfo.Title!, discName, parsedDiscInfo, useAutoIncrement);

        var discInfo = _stateManager.GetNextDiscInfo(seriesState, discName, rippedTracks.Count, parsedDiscInfo, useAutoIncrement, rippedTracks);
        
        if (!useAutoIncrement && CheckIfReprocessing(seriesState, discName))
        {
            isReprocessing = true;
            _logger.LogWarning($"Re-processing disc {discName}. Using existing episode mapping.");
            _consoleInteraction.ResetErrorPrompting();
        }

        await ConfigureTrackSortingStrategyAsync(seriesState, seriesInfo.Title!);

        var sortedTracks = SortTracksForEpisodeProcessing(rippedTracks, seriesState, discInfo);
        
        var success = await ProcessTracksToEpisodesAsync(outputPath, discName, sortedTracks, seriesInfo, seriesState, discInfo, isReprocessing);

        await FinalizeSeriesProcessingAsync(seriesState, discInfo, sortedTracks, rippedTracks, useAutoIncrement);

        return success;
    }

    private async Task<(bool useAutoIncrement, bool isReprocessing)> ConfigureAutoIncrementModeAsync(SeriesState seriesState, string seriesTitle, string discName)
    {
        bool useAutoIncrement = false;
        bool isReprocessing = false;

        if (seriesState.AutoIncrementPreference.HasValue)
        {
            useAutoIncrement = seriesState.AutoIncrementPreference.Value;
            if (useAutoIncrement)
            {
                seriesState.AutoIncrement = true;
                _logger.LogInformation($"Using saved Auto Increment preference for {seriesTitle}: {(useAutoIncrement ? "Enabled" : "Disabled")}");
            }
        }
        else if (seriesState.AutoIncrement)
        {
            useAutoIncrement = _consoleInteraction.PromptForAutoIncrementWhenEnabled(seriesTitle, discName);
            seriesState.AutoIncrementPreference = useAutoIncrement;
            await _stateManager.SaveSeriesStateAsync(seriesState);
            _logger.LogInformation($"Saved Auto Increment preference for {seriesTitle}: {(useAutoIncrement ? "Enabled" : "Disabled")}");
        }
        else if (HasSimilarDiscName(seriesState, discName))
        {
            var enableAutoIncrement = _consoleInteraction.PromptForAutoIncrement(seriesTitle, discName);
            if (enableAutoIncrement)
            {
                seriesState.AutoIncrement = true;
                useAutoIncrement = true;
                seriesState.AutoIncrementPreference = true;
                await _stateManager.SaveSeriesStateAsync(seriesState);
                _logger.LogInformation($"Auto Increment mode enabled for series: {seriesTitle}");
            }
            else
            {
                seriesState.AutoIncrementPreference = false;
                await _stateManager.SaveSeriesStateAsync(seriesState);
                _logger.LogInformation($"Auto Increment mode disabled by user for series: {seriesTitle}");
            }
        }

        return (useAutoIncrement, isReprocessing);
    }

    private Task HandleSeasonEpisodePromptsAsync(SeriesState seriesState, string seriesTitle, string discName, ParsedDiscInfo parsedDiscInfo, bool useAutoIncrement)
    {
        if (!useAutoIncrement &&
            !seriesState.ProcessedDiscs.Any() &&
            (parsedDiscInfo.Season <= 1 && string.IsNullOrEmpty(parsedDiscInfo.SeriesName)))
        {
            _logger.LogInformation($"No previous state found for {seriesTitle} and season/episode info unclear. Prompting user.");
            var (userSeason, userEpisode) = _seriesConfigurationService.PromptForStartingSeasonAndEpisode(seriesTitle, discName);

            parsedDiscInfo.Season = userSeason;
            seriesState.CurrentSeason = userSeason;
            seriesState.NextEpisode = userEpisode;
        }
        
        return Task.CompletedTask;
    }

    private async Task HandleSeasonMismatchAsync(SeriesState seriesState, ParsedDiscInfo parsedDiscInfo, string seriesTitle, string discName)
    {
        _logger.LogDebug($"Checking season mismatch: ParsedSeason={parsedDiscInfo.Season}, StateSeason={seriesState.CurrentSeason}");
        
        if (parsedDiscInfo.Season > 0 && seriesState.CurrentSeason != parsedDiscInfo.Season)
        {
            _logger.LogWarning($"Season mismatch detected: State shows Season {seriesState.CurrentSeason}, disc shows Season {parsedDiscInfo.Season}");
            
            var result = _consoleInteraction.PromptForSeasonMismatchResolution(
                seriesTitle, 
                discName, 
                seriesState.CurrentSeason, 
                parsedDiscInfo.Season);

            if (result.Success && result.Value)
            {
                _logger.LogInformation($"User chose to update season from {seriesState.CurrentSeason} to {parsedDiscInfo.Season}");
                seriesState.CurrentSeason = parsedDiscInfo.Season;
                seriesState.NextEpisode = 1;
                await _stateManager.SaveSeriesStateAsync(seriesState);
            }
            else if (result.Success && !result.Value)
            {
                _logger.LogInformation($"User chose to continue with current season {seriesState.CurrentSeason}");
                parsedDiscInfo.Season = seriesState.CurrentSeason;
            }
            else
            {
                _logger.LogWarning("User cancelled season mismatch resolution, using current state season");
                parsedDiscInfo.Season = seriesState.CurrentSeason;
            }
        }
    }

    private bool CheckIfReprocessing(SeriesState seriesState, string discName)
    {
        return seriesState.ProcessedDiscs.Any(d => d.DiscName.Equals(discName, StringComparison.OrdinalIgnoreCase));
    }

    private async Task ConfigureTrackSortingStrategyAsync(SeriesState seriesState, string seriesTitle)
    {
        if (!seriesState.TrackSortingStrategy.HasValue)
        {
            var strategy = _seriesConfigurationService.PromptForTrackSortingStrategy(seriesTitle);
            seriesState.TrackSortingStrategy = strategy;
            await _stateManager.SaveSeriesStateAsync(seriesState);
        }
    }

    private List<AkTitle> SortTracksForEpisodeProcessing(List<AkTitle> rippedTracks, SeriesState seriesState, DiscInfo discInfo)
    {
        List<AkTitle> sortedTracks;

        if (seriesState.TrackSortingStrategy == TrackSortingStrategy.ByMplsFileName)
        {
            sortedTracks = rippedTracks
                .Where(t => !string.IsNullOrEmpty(t.SourceFileName))
                .OrderBy(t => t.SourceFileName)
                .ToList();

            var tracksWithoutMpls = rippedTracks
                .Where(t => string.IsNullOrEmpty(t.SourceFileName))
                .OrderBy(t => int.TryParse(t.Id, out var id) ? id : 0)
                .ToList();

            sortedTracks.AddRange(tracksWithoutMpls);
            _logger.LogInformation($"Sorted {sortedTracks.Count} tracks by MPLS file name for episode ordering");
        }
        else if (seriesState.TrackSortingStrategy == TrackSortingStrategy.UserConfirmed)
        {
            sortedTracks = rippedTracks.OrderBy(t => int.TryParse(t.Id, out var id) ? id : 0).ToList();
            _logger.LogInformation($"Using user confirmed strategy for {sortedTracks.Count} tracks");

            // This will be handled in ProcessTracksToEpisodesAsync for UserConfirmed strategy
        }
        else
        {
            sortedTracks = rippedTracks.OrderBy(t => int.TryParse(t.Id, out var id) ? id : 0).ToList();
            _logger.LogInformation($"Sorted {sortedTracks.Count} tracks by track order for episode ordering");
        }

        return sortedTracks;
    }

    private async Task<bool> ProcessTracksToEpisodesAsync(string outputPath, string discName, List<AkTitle> sortedTracks, SeriesInfo seriesInfo, SeriesState seriesState, DiscInfo discInfo, bool isReprocessing)
    {
        bool success = true;

        if (seriesState.TrackSortingStrategy == TrackSortingStrategy.UserConfirmed)
        {
            var (confirmedTracks, userEpisodeMapping, userSelections, skippedTracks) = await HandleUserConfirmedSorting(sortedTracks, seriesInfo, seriesState, discName);
            sortedTracks = confirmedTracks;
            
            // Handle skipped tracks by moving them to _trash folder
            if (skippedTracks.Count > 0)
            {
                MoveSkippedTracksToTrash(outputPath, skippedTracks);
            }
            
            discInfo.TrackToEpisodeMapping.Clear();
            foreach (var kvp in userEpisodeMapping)
            {
                discInfo.TrackToEpisodeMapping[kvp.Key] = new List<int> { kvp.Value };
            }
            
            // Store user selections for pattern learning
            discInfo.UserSelections = userSelections;
        }

        var minEpisodeLength = sortedTracks.Min(t => t.LengthInSeconds);
        int currentEpisode = discInfo.StartingEpisode;
        var trackToEpisodeMapping = discInfo.TrackToEpisodeMapping;

        for (int i = 0; i < sortedTracks.Count; i++)
        {
            var track = sortedTracks[i];
            var episodeNumbers = DetermineEpisodeNumbers(track, i, trackToEpisodeMapping, ref currentEpisode, minEpisodeLength, seriesState, seriesInfo.Title!);

            bool trackSuccess = await ProcessSingleTrackAsync(outputPath, discName, track, seriesInfo, discInfo, episodeNumbers[0], isReprocessing, minEpisodeLength, currentEpisode);
            
            if (!trackSuccess)
            {
                success = false;
            }

            // Update mapping for this track
            trackToEpisodeMapping[i] = episodeNumbers;
        }

        discInfo.TrackToEpisodeMapping = trackToEpisodeMapping;
        return success;
    }

    private List<int> DetermineEpisodeNumbers(AkTitle track, int trackIndex, Dictionary<int, List<int>> trackToEpisodeMapping, ref int currentEpisode, double minEpisodeLength, SeriesState seriesState, string seriesTitle)
    {
        if (trackToEpisodeMapping.ContainsKey(trackIndex))
        {
            var existingEpisodes = trackToEpisodeMapping[trackIndex];
            _logger.LogInformation($"Using {(seriesState.TrackSortingStrategy == TrackSortingStrategy.UserConfirmed ? "user-confirmed" : "existing")} episode mapping: Track {track.Id} -> Episode {existingEpisodes[0]}");
            return existingEpisodes;
        }

        bool treatAsDouble = DetermineIfDoubleEpisode(track, minEpisodeLength, seriesState, seriesTitle);

        if (treatAsDouble)
        {
            var episodes = new List<int> { currentEpisode, currentEpisode + 1 };
            currentEpisode += 2;
            return episodes;
        }
        else
        {
            var episodes = new List<int> { currentEpisode };
            currentEpisode++;
            return episodes;
        }
    }

    private bool DetermineIfDoubleEpisode(AkTitle track, double minEpisodeLength, SeriesState seriesState, string seriesTitle)
    {
        var doubleEpisodeHandling = seriesState.DoubleEpisodeHandling ?? DoubleEpisodeHandling.AlwaysAsk;

        switch (doubleEpisodeHandling)
        {
            case DoubleEpisodeHandling.AlwaysAsk:
                if (track.LengthInSeconds > 1.5 * minEpisodeLength)
                {
                    var (userTreatAsDouble, savePreference) = _seriesConfigurationService.PromptForDoubleEpisodeHandling(
                        seriesTitle,
                        track.Name,
                        track.LengthInSeconds,
                        minEpisodeLength);

                    if (savePreference.HasValue)
                    {
                        seriesState.DoubleEpisodeHandling = savePreference.Value;
                        _ = Task.Run(async () => await _stateManager.SaveSeriesStateAsync(seriesState));
                    }

                    return userTreatAsDouble;
                }
                return false;

            case DoubleEpisodeHandling.AlwaysSingle:
                _logger.LogInformation($"Track {track.Id} treated as single episode per series preference");
                return false;

            case DoubleEpisodeHandling.AlwaysDouble:
                bool isDouble = track.LengthInSeconds > 1.5 * minEpisodeLength;
                if (isDouble)
                {
                    _logger.LogInformation($"Track {track.Id} treated as double episode per series preference");
                }
                return isDouble;

            default:
                return false;
        }
    }

    private async Task<bool> ProcessSingleTrackAsync(string outputPath, string discName, AkTitle track, SeriesInfo seriesInfo, DiscInfo discInfo, int episodeNumber, bool isReprocessing, double minEpisodeLength, int currentEpisode)
    {
        try
        {
            var cachedEpisodeInfo = await _enhancedOmdbService.GetEpisodeInfoAsync(
                seriesInfo.Title!,
                discInfo.Season,
                episodeNumber);

            var originalFile = _fileDiscoveryService.FindRippedFile(outputPath, track, discName);
            if (originalFile == null)
            {
                _logger.LogWarning($"Could not find ripped file for track: {track.Id}");
                
                var choice = _consoleInteraction.HandleEpisodeProcessingError(
                    seriesInfo.Title!,
                    discInfo.Season,
                    episodeNumber,
                    "Could not find ripped file"
                );

                if (choice == ConsoleInteractionService.ErrorHandlingChoice.Exit)
                {
                    _logger.LogInformation("User chose to exit application");
                    Environment.Exit(0);
                }

                return false;
            }

            var newFileName = _namingService.GenerateEpisodeFileName(
                seriesInfo.Title!,
                discInfo.Season,
                episodeNumber,
                cachedEpisodeInfo?.Title,
                Path.GetExtension(originalFile));

            var newFilePath = Path.Combine(
                _namingService.GetSeriesDirectory(outputPath, seriesInfo.Title!),
                _namingService.GetSeasonDirectory(discInfo.Season),
                newFileName);

            FileSystemHelper.EnsureFileDirectoryExists(newFilePath);
            
            // Try to move the file with retry logic for access errors
            bool moveSuccessful = false;
            int retryCount = 0;
            const int maxRetries = 3;
            
            while (!moveSuccessful && retryCount < maxRetries)
            {
                try
                {
                    File.Move(originalFile, newFilePath);
                    moveSuccessful = true;
                }
                catch (IOException ioEx) when (ioEx.Message.Contains("being used by another process") || 
                                              ioEx.Message.Contains("Access to the path") ||
                                              ioEx.Message.Contains("denied"))
                {
                    retryCount++;
                    
                    if (retryCount >= maxRetries)
                    {
                        // Ask user what to do
                        var userChoice = _consoleInteraction.HandleFileAccessError(
                            seriesInfo.Title!,
                            discInfo.Season,
                            episodeNumber,
                            Path.GetFileName(originalFile),
                            ioEx.Message);
                        
                        switch (userChoice)
                        {
                            case ConsoleInteractionService.FileAccessErrorChoice.Retry:
                                retryCount = 0; // Reset retry count
                                continue;
                                
                            case ConsoleInteractionService.FileAccessErrorChoice.Skip:
                                _logger.LogWarning($"User chose to skip file: {Path.GetFileName(originalFile)}");
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
                        _logger.LogInformation($"Waiting 2 seconds before retry...");
                        await Task.Delay(2000); // Wait 2 seconds before retry
                    }
                }
                catch (Exception)
                {
                    // For other exceptions, throw immediately
                    throw;
                }
            }
            
            if (moveSuccessful)
            {
                _consoleOutput.ShowFileRenamed(Path.GetFileName(originalFile), newFileName);
                _consoleOutput.ShowFilesMovedTo(Path.GetDirectoryName(newFilePath)!);
                _logger.LogInformation($"Renamed: {Path.GetFileName(originalFile)} -> {newFileName} (Episode {episodeNumber})");
            }

            return moveSuccessful;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error processing episode {episodeNumber} of {seriesInfo.Title}");

            var choice = _consoleInteraction.HandleEpisodeProcessingError(
                seriesInfo.Title!,
                discInfo.Season,
                episodeNumber,
                ex.Message
            );

            if (choice == ConsoleInteractionService.ErrorHandlingChoice.Exit)
            {
                _logger.LogInformation("User chose to exit application");
                Environment.Exit(0);
            }

            // Note: Episode counter management is handled in the calling method

            return false;
        }
    }

    private async Task FinalizeSeriesProcessingAsync(SeriesState seriesState, DiscInfo discInfo, List<AkTitle> sortedTracks, List<AkTitle> rippedTracks, bool useAutoIncrement)
    {
        discInfo.TrackCount = sortedTracks.Count;
        var actualEpisodeCount = discInfo.TrackToEpisodeMapping.Values.SelectMany(eps => eps).Count();

        await _stateManager.UpdateSeriesStateAsync(seriesState, discInfo, actualEpisodeCount, rippedTracks, useAutoIncrement);
        
        // If using UserConfirmed strategy and we have user selections, complete pattern learning
        if (seriesState.TrackSortingStrategy == TrackSortingStrategy.UserConfirmed && 
            discInfo.UserSelections?.Any() == true)
        {
            _logger.LogInformation($"Completing pattern learning for {seriesState.SeriesTitle} with {discInfo.UserSelections.Count} selections");
            await _seriesConfigurationService.CompletePatternLearningAsync(
                seriesState.SeriesTitle, 
                discInfo.Season, 
                discInfo.DiscName, 
                discInfo.UserSelections);
        }
        
        await _stateManager.SaveSeriesStateAsync(seriesState);
    }

    private async Task<bool> ProcessMovie(string outputPath, string discName, List<AkTitle> rippedTracks, MovieInfo movieInfo)
    {
        _logger.LogInformation($"Processing Movie: {movieInfo.Title}");

        var success = true;

        foreach (var track in rippedTracks)
        {
            try
            {
                var originalFile = _fileDiscoveryService.FindRippedFile(outputPath, track);
                if (originalFile == null)
                {
                    _logger.LogWarning($"Could not find ripped file for track: {track.Id}");
                    success = false;
                    continue;
                }

                var newFileName = _namingService.GenerateMovieFileName(
                    movieInfo.Title!,
                    movieInfo.Year,
                    Path.GetExtension(originalFile));

                var movieDir = _namingService.GetMovieDirectory(outputPath, movieInfo.Title!, movieInfo.Year);
                var newFilePath = Path.Combine(movieDir, newFileName);

                // Ensure directory exists
                FileSystemHelper.EnsureDirectoryExists(movieDir);

                // Try to move the file with retry logic for access errors
                bool moveSuccessful = false;
                int retryCount = 0;
                const int maxRetries = 3;
                
                while (!moveSuccessful && retryCount < maxRetries)
                {
                    try
                    {
                        File.Move(originalFile, newFilePath);
                        moveSuccessful = true;
                    }
                    catch (IOException ioEx) when (ioEx.Message.Contains("being used by another process") || 
                                                  ioEx.Message.Contains("Access to the path") ||
                                                  ioEx.Message.Contains("denied"))
                    {
                        retryCount++;
                        
                        if (retryCount >= maxRetries)
                        {
                            // Ask user what to do
                            var userChoice = _consoleInteraction.HandleFileAccessError(
                                movieInfo.Title!,
                                0, // No season for movies
                                1, // Use episode 1 as default for movies
                                Path.GetFileName(originalFile),
                                ioEx.Message);
                            
                            switch (userChoice)
                            {
                                case ConsoleInteractionService.FileAccessErrorChoice.Retry:
                                    retryCount = 0; // Reset retry count
                                    continue;
                                    
                                case ConsoleInteractionService.FileAccessErrorChoice.Skip:
                                    _logger.LogWarning($"User chose to skip file: {Path.GetFileName(originalFile)}");
                                    success = false;
                                    moveSuccessful = true; // Exit the retry loop
                                    break;
                                    
                                case ConsoleInteractionService.FileAccessErrorChoice.Exit:
                                    _logger.LogInformation("User chose to exit application");
                                    Environment.Exit(0);
                                    break;
                            }
                        }
                        else
                        {
                            _logger.LogWarning($"File access error (attempt {retryCount}/{maxRetries}): {ioEx.Message}");
                            _logger.LogInformation($"Waiting 2 seconds before retry...");
                            await Task.Delay(2000); // Wait 2 seconds before retry
                        }
                    }
                    catch (Exception)
                    {
                        // For other exceptions, throw immediately
                        throw;
                    }
                }
                
                if (moveSuccessful && success) // Only show success messages if move was successful and we haven't marked as failed
                {
                    _consoleOutput.ShowFileRenamed(Path.GetFileName(originalFile), newFileName);
                    _consoleOutput.ShowFilesMovedTo(movieDir);
                    _logger.LogInformation($"Renamed: {Path.GetFileName(originalFile)} -> {newFileName}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing movie track {track.Id} for {movieInfo.Title}");
                success = false;
            }
        }

        return success;
    }

    private async Task<bool> ProcessUnknownMediaAsync(string outputPath, string discName, List<AkTitle> rippedTracks)
    {
        _logger.LogWarning($"Could not identify media type for {discName}, attempting search");

        // Try searching for movies with different strategies
        var searchResults = await _omdbClient.SearchMovie(CleanDiscName(discName), null);
        if (searchResults?.Any() == true)
        {
            var bestMatch = searchResults.First();
            _logger.LogInformation($"Found potential match: {bestMatch.Title} ({bestMatch.Year})");

            // Get full movie info and process
            var fullMovieInfo = await _omdbClient.GetMovie(bestMatch.Title!, int.TryParse(bestMatch.Year, out var year) ? year : null);
            if (fullMovieInfo != null)
            {
                var movieInfo = MovieInfo.FromMovieResponse(fullMovieInfo);
                return await ProcessMovie(outputPath, discName, rippedTracks, movieInfo);
            }
        }

        // If automatic search failed, try interactive search
        _logger.LogInformation("Automatic search failed, initiating interactive search");
        var searchTitle = CleanDiscName(discName);
        var mediaInfo = await _mediaSelectionService.InteractiveMediaSearchAsync(searchTitle, discName);

        if (mediaInfo == null)
        {
            _logger.LogWarning($"Could not identify or process media for disc: {discName}");
            return false;
        }

        // Save the manual identification for future use
        var mediaIdentity = ModelConverter.ToMediaIdentity(mediaInfo);
        await _stateManager.SaveManualIdentificationAsync(discName, mediaIdentity);
        _logger.LogInformation($"Saved manual identification for future discs matching pattern.");

        // Process based on the type of media found
        if (mediaInfo.Type?.Equals("series", StringComparison.OrdinalIgnoreCase) == true)
        {
            var seriesInfo = new SeriesInfo
            {
                Title = mediaInfo.Title,
                Year = mediaInfo.Year,
                Type = mediaInfo.Type,
                ImdbID = mediaInfo.ImdbID
            };
            return await ProcessTvSeriesAsync(outputPath, discName, rippedTracks, seriesInfo);
        }
        else
        {
            var movieInfo = new MovieInfo
            {
                Title = mediaInfo.Title,
                Year = mediaInfo.Year,
                Type = mediaInfo.Type,
                ImdbID = mediaInfo.ImdbID
            };
            return await ProcessMovie(outputPath, discName, rippedTracks, movieInfo);
        }
    }


    private string CleanDiscName(string discName)
    {
        // Remove common disc identifiers and clean up
        var cleaned = discName
            .Replace("_", " ")
            .Replace("-", " ")
            .Trim();

        // Remove disc/season indicators but preserve the info for parsing
        // Don't remove these here since we need them for parsing

        // Remove multiple spaces
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

        return cleaned;
    }

    private string ExtractSeriesNameForSearch(string discName)
    {
        // Clean up disc name for OMDB searching by removing all disc/season/format identifiers
        var cleaned = discName
            .Replace("_", " ")
            .Replace("-", " ")
            .Trim();

        // Remove various disc/format identifiers
        cleaned = Regex.Replace(cleaned, @"\b(disc|disk|cd|dvd|bd|bluray|blu-ray)\b", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\b(season|s)\s*\d+\b", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\b[ds]\d+\b", "", RegexOptions.IgnoreCase); // Remove D1, D2, S8, etc.
        cleaned = Regex.Replace(cleaned, @"\b(part|pt)\s*\d+\b", "", RegexOptions.IgnoreCase);

        // Remove multiple spaces and trim
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

        return cleaned;
    }

    private ParsedDiscInfo ParseDiscName(string discName)
    {
        var result = new ParsedDiscInfo();

        // Pattern to match "Frasier_S8_D1_BD" or "Series_Name_S8_D1" etc.
        var patterns = new[]
        {
            @"^(.+?)_[Ss](\d+)_[Dd](\d+)", // Frasier_S8_D1_BD
            @"^(.+?)[_\s][Ss](\d+)[_\s][Dd](\d+)", // Frasier S8 D1 or Frasier_S8_D1
            @"(.+?)\s*[Ss](\d+)[^0-9]*[Dd](\d+)", // Frasier S8 D1 (original pattern)
            @"(.+?)\s*Season\s*(\d+).*Disc\s*(\d+)" // Frasier Season 8 Disc 1
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(discName, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                result.SeriesName = match.Groups[1].Value.Replace("_", " ").Trim();
                result.Season = int.Parse(match.Groups[2].Value);
                result.DiscNumber = int.Parse(match.Groups[3].Value);

                _logger.LogInformation($"Parsed disc name '{discName}': Series='{result.SeriesName}', Season={result.Season}, Disc={result.DiscNumber}");
                return result;
            }
        }

        // If no pattern matches, try to extract series name anyway
        var fallbackMatch = Regex.Match(discName, @"^(.+?)(?:[_\s][Ss]?\d+|[_\s]Season)", RegexOptions.IgnoreCase);
        if (fallbackMatch.Success)
        {
            result.SeriesName = fallbackMatch.Groups[1].Value.Replace("_", " ").Trim();
        }
        else
        {
            result.SeriesName = ExtractSeriesNameForSearch(discName);
        }

        // Use defaults for season/disc if not parsed
        result.Season = 1;
        result.DiscNumber = 1;

        _logger.LogWarning($"Could not fully parse season/disc info from '{discName}', extracted series name: '{result.SeriesName}'");
        return result;
    }

    private (string title, int? year) ExtractTitleAndYear(string input)
    {
        var yearMatch = Regex.Match(input, @"\b(19|20)\d{2}\b");
        if (yearMatch.Success)
        {
            var year = int.Parse(yearMatch.Value);
            var title = input.Replace(yearMatch.Value, "").Trim();
            return (title, year);
        }

        return (input, null);
    }

    private bool HasSimilarDiscName(SeriesState seriesState, string discName)
    {
        // Extract base name for comparison
        var baseName = ExtractSeriesNameForSearch(discName);

        // Check if any processed disc has a similar base name
        return seriesState.ProcessedDiscs.Any(d =>
        {
            var existingBaseName = ExtractSeriesNameForSearch(d.DiscName);
            return existingBaseName.Equals(baseName, StringComparison.OrdinalIgnoreCase);
        });
    }

    private PreIdentifiedMedia CreatePreIdentifiedMedia(MediaIdentity? mediaData, bool isFromCache, string identificationSource)
    {
        return new PreIdentifiedMedia
        {
            MediaData = mediaData,
            IsFromCache = isFromCache,
            IdentificationSource = identificationSource
        };
    }

    private bool IsValidOmdbResult(MediaIdentity? result)
    {
        return result.IsValidOmdbResponse();
    }
    
    private bool IsValidConfirmationInfo(ConfirmationInfo? result)
    {
        return result.IsValidOmdbResponse();
    }
    
    private bool IsValidMediaIdentity(MediaIdentity? result)
    {
        return result.IsValidOmdbResponse();
    }

    private async Task<(ConfirmationInfo? series, ConfirmationInfo? movie)> SearchForMediaInOmdbAsync(string searchTitle)
    {
        var seriesTask = _omdbClient.GetSeries(searchTitle);
        var movieTask = _omdbClient.GetMovie(searchTitle, null);

        await Task.WhenAll(seriesTask, movieTask);

        var seriesResult = await seriesTask;
        var movieResult = await movieTask;
        return (seriesResult != null ? ModelConverter.ToConfirmationInfo(seriesResult) : null, 
                movieResult != null ? ModelConverter.ToConfirmationInfo(movieResult) : null);
    }

    private async Task<PreIdentifiedMedia?> HandleBothMediaTypesFoundAsync(string discName, ConfirmationInfo movieResult, ConfirmationInfo seriesResult)
    {
        _logger.LogInformation($"Found both movie and TV series matches");
        var selectedResult = await _mediaSelectionService.SelectBetweenMovieAndSeriesAsync(discName, movieResult, seriesResult);

        if (selectedResult != null)
        {
            await _stateManager.SaveManualIdentificationAsync(discName, selectedResult);
            return CreatePreIdentifiedMedia(selectedResult, false, "interactive");
        }
        return null;
    }

    private async Task<PreIdentifiedMedia?> HandleSeriesFoundAsync(string discName, string searchTitle, ConfirmationInfo seriesResult)
    {
        _logger.LogInformation($"Identified as TV Series: {seriesResult.Title}");

        var existingSeriesState = await _stateManager.GetExistingSeriesStateAsync(seriesResult.Title!);
        if (existingSeriesState != null)
        {
            _logger.LogInformation($"Found existing state for {seriesResult.Title} - skipping confirmation prompt");
            return CreatePreIdentifiedMedia(ModelConverter.ToMediaIdentity(seriesResult), false, "automatic");
        }

        return await ConfirmAndProcessIdentificationAsync(discName, searchTitle, seriesResult, false);
    }

    private async Task<PreIdentifiedMedia?> HandleMovieFoundAsync(string discName, string searchTitle, ConfirmationInfo movieResult, bool isAutoMode)
    {
        _logger.LogInformation($"Identified as Movie: {movieResult.Title}");

        var existingManualId = await _stateManager.GetManualIdentificationAsync(discName);
        if (existingManualId != null)
        {
            _logger.LogInformation($"Found existing manual identification for {discName} - skipping confirmation prompt");
            return CreatePreIdentifiedMedia(ModelConverter.ToMediaIdentity(movieResult), false, "automatic");
        }

        // In auto mode, skip confirmation for movies and proceed automatically
        if (isAutoMode)
        {
            _logger.LogInformation($"Auto mode enabled - skipping confirmation for movie: {movieResult.Title}");
            var mediaIdentity = ModelConverter.ToMediaIdentity(movieResult);
            await _stateManager.SaveManualIdentificationAsync(discName, mediaIdentity);
            return CreatePreIdentifiedMedia(mediaIdentity, false, "automatic");
        }

        return await ConfirmAndProcessIdentificationAsync(discName, searchTitle, movieResult, true);
    }

    private async Task<PreIdentifiedMedia?> ConfirmAndProcessIdentificationAsync(string discName, string searchTitle, ConfirmationInfo mediaData, bool saveOnConfirm)
    {
        if (!_mediaSelectionService.ConfirmMediaIdentification(mediaData, discName))
        {
            _logger.LogInformation("User rejected automatic identification, falling back to interactive search");
            var interactiveResult = await _mediaSelectionService.InteractiveMediaSearchAsync(searchTitle, discName);

            if (interactiveResult != null)
            {
                var mediaIdentity = ModelConverter.ToMediaIdentity(interactiveResult);
                await _stateManager.SaveManualIdentificationAsync(discName, mediaIdentity);
                return CreatePreIdentifiedMedia(mediaIdentity, false, "interactive");
            }
            return null;
        }

        if (saveOnConfirm)
        {
            var mediaIdentity = ModelConverter.ToMediaIdentity(mediaData);
            await _stateManager.SaveManualIdentificationAsync(discName, mediaIdentity);
        }

        return CreatePreIdentifiedMedia(ModelConverter.ToMediaIdentity(mediaData), false, "automatic");
    }

    private async Task<PreIdentifiedMedia?> TrySearchMovieWithYearAsync(string discName, string searchTitle, bool isAutoMode)
    {
        var (title, year) = ExtractTitleAndYear(searchTitle);
        if (!year.HasValue)
        {
            return null;
        }

        var movieWithYear = await _omdbClient.GetMovie(title, year);
        var movieData = movieWithYear != null ? ModelConverter.ToConfirmationInfo(movieWithYear) : null;
        var movieIdentity = movieData != null ? ModelConverter.ToMediaIdentity(movieData) : null;
        if (!IsValidOmdbResult(movieIdentity))
        {
            return null;
        }

        _logger.LogInformation($"Identified as Movie with year: {movieData.Title} ({year})");

        var existingManualId = await _stateManager.GetManualIdentificationAsync(discName);
        if (existingManualId != null)
        {
            _logger.LogInformation($"Found existing manual identification - skipping confirmation prompt");
            return CreatePreIdentifiedMedia(ModelConverter.ToMediaIdentity(movieData), false, "automatic");
        }

        // In auto mode, skip confirmation for movies and proceed automatically
        if (isAutoMode)
        {
            _logger.LogInformation($"Auto mode enabled - skipping confirmation for movie: {movieData.Title}");
            await _stateManager.SaveManualIdentificationAsync(discName, movieIdentity!);
            return CreatePreIdentifiedMedia(movieIdentity, false, "automatic");
        }

        return await ConfirmAndProcessIdentificationAsync(discName, searchTitle, movieData, true);
    }

    private async Task<PreIdentifiedMedia?> HandleNoAutomaticMatchAsync(string discName, string searchTitle)
    {
        _logger.LogWarning($"Could not automatically identify media for disc: {discName}");
        var interactiveResult = await _mediaSelectionService.InteractiveMediaSearchAsync(searchTitle, discName);

        if (interactiveResult != null)
        {
            var mediaIdentity = ModelConverter.ToMediaIdentity(interactiveResult);
            await _stateManager.SaveManualIdentificationAsync(discName, mediaIdentity);
            return CreatePreIdentifiedMedia(mediaIdentity, false, "interactive");
        }

        return null;
    }

    private async Task<(List<AkTitle> tracks, Dictionary<int, int> trackToEpisodeMap, List<TrackSelectionPattern> userSelections, List<AkTitle> skippedTracks)> HandleUserConfirmedSorting(List<AkTitle> tracks, SeriesInfo seriesInfo, SeriesState seriesState, string discName)
    {
        _logger.LogInformation($"Starting user confirmation for {tracks.Count} tracks in {seriesInfo.Title}");

        var reorderedTracks = new List<AkTitle>();
        var trackToEpisodeMap = new Dictionary<int, int>(); // Maps track index to episode number
        var userSelections = new List<TrackSelectionPattern>(); // For pattern learning
        var skippedTracks = new List<AkTitle>(); // Tracks marked for skipping (_trash folder)
        var availableEpisodes = new List<int>();

        // Calculate the maximum reasonable episode number for this season
        // Use the total episodes for the series if available, otherwise use a reasonable default
        var maxEpisodeForSeason = 50; // Default fallback

        // Try to get season info from enhanced OMDB service to know episode count
        try
        {
            var seasonCache = await _enhancedOmdbService.GetOrFetchSeasonInfoAsync(seriesInfo.Title!, seriesState.CurrentSeason);
            if (seasonCache != null && seasonCache.Episodes.Any())
            {
                maxEpisodeForSeason = seasonCache.Episodes.Count;
                _logger.LogInformation($"Season {seriesState.CurrentSeason} has {maxEpisodeForSeason} episodes according to OMDB data");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Could not fetch season info for episode count, using default max of {maxEpisodeForSeason}");
        }

        // Create list of available episodes for this batch only
        // If we have 5 tracks and start at episode 4, we can only assign episodes 4-8
        var startingEpisode = seriesState.NextEpisode;
        var maxEpisodeForThisBatch = startingEpisode + tracks.Count - 1;
        
        for (int i = startingEpisode; i <= maxEpisodeForThisBatch; i++)
        {
            availableEpisodes.Add(i);
        }
        
        _logger.LogInformation($"Available episodes for this batch of {tracks.Count} tracks: {startingEpisode} to {maxEpisodeForThisBatch}");

        // Confirm each track with the user
        foreach (var track in tracks)
        {
            var suggestedEpisode = seriesState.NextEpisode + reorderedTracks.Count;

            // Ensure the suggested episode is available
            if (!availableEpisodes.Contains(suggestedEpisode))
            {
                if (availableEpisodes.Any())
                {
                    suggestedEpisode = availableEpisodes.First();
                }
                else
                {
                    _logger.LogWarning("No more available episodes for assignment");
                    break;
                }
            }

            // Try to get episode title from enhanced OMDB service
            string episodeTitle = "";
            try
            {
                var episodeInfo = await _enhancedOmdbService.GetEpisodeInfoAsync(
                    seriesInfo.Title!,
                    seriesState.CurrentSeason,
                    suggestedEpisode);

                if (episodeInfo != null)
                {
                    episodeTitle = episodeInfo.Title;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Could not fetch episode title for S{seriesState.CurrentSeason:D2}E{suggestedEpisode:D2}");
            }

            // Track position for pattern learning
            var trackPosition = reorderedTracks.Count;
            
            // Ask user to confirm or select different episode (with pattern learning support)
            var selectedEpisode = await _seriesConfigurationService.ConfirmOrSelectEpisodeWithPatternLearningAsync(
                seriesInfo.Title!,
                seriesState.CurrentSeason,
                suggestedEpisode,
                episodeTitle,
                track.Name,
                availableEpisodes.ToList(),
                _enhancedOmdbService,
                tracks,
                discName,
                track.Id,
                trackPosition);
            
            // Handle skipped tracks
            if (selectedEpisode == null)
            {
                // Create selection pattern for skipped track
                var skippedSelection = new TrackSelectionPattern
                {
                    TrackId = track.Id,
                    TrackName = track.Name,
                    TrackOrderPosition = trackPosition,
                    SuggestedEpisode = suggestedEpisode,
                    SelectedEpisode = -1, // Use -1 to indicate skipped
                    WasAccepted = false,
                    SelectionDate = DateTime.Now,
                    SelectionReason = "skipped"
                };
                userSelections.Add(skippedSelection);
                
                // Add track to skipped list for _trash folder processing
                skippedTracks.Add(track);
                _logger.LogInformation($"Track {track.Name} marked for skipping - will be moved to _trash folder");
                continue; // Skip to next track
            }
            
            // Create selection pattern for learning
            var selection = new TrackSelectionPattern
            {
                TrackId = track.Id,
                TrackName = track.Name,
                TrackOrderPosition = trackPosition,
                SuggestedEpisode = suggestedEpisode,
                SelectedEpisode = selectedEpisode.Value,
                WasAccepted = selectedEpisode.Value == suggestedEpisode,
                SelectionDate = DateTime.Now,
                SelectionReason = selectedEpisode.Value == suggestedEpisode ? "accepted" : "manual_choice"
            };
            userSelections.Add(selection);

            // Remove the selected episode from available episodes
            availableEpisodes.Remove(selectedEpisode.Value);

            // Store the track with its confirmed episode assignment
            var trackIndex = reorderedTracks.Count;
            reorderedTracks.Add(track);
            trackToEpisodeMap[trackIndex] = selectedEpisode.Value;

            _logger.LogInformation($"Track {track.Name} confirmed as episode {selectedEpisode.Value}");
        }

        _logger.LogInformation($"User confirmation complete. Confirmed {reorderedTracks.Count} tracks, skipped {skippedTracks.Count} tracks, with {userSelections.Count} selections for pattern learning");
        return (reorderedTracks, trackToEpisodeMap, userSelections, skippedTracks);
    }

    /// <summary>
    /// Moves skipped tracks to a _trash folder within the output directory
    /// </summary>
    /// <param name="outputPath">The base output path</param>
    /// <param name="skippedTracks">List of tracks to move to trash</param>
    private void MoveSkippedTracksToTrash(string outputPath, List<AkTitle> skippedTracks)
    {
        try
        {
            // Create _trash folder in the output directory
            var trashFolderPath = Path.Combine(outputPath, "_trash");
            Directory.CreateDirectory(trashFolderPath);
            
            _logger.LogInformation($"Moving {skippedTracks.Count} skipped tracks to _trash folder: {trashFolderPath}");
            
            foreach (var track in skippedTracks)
            {
                try
                {
                    // Use file discovery service to find the ripped file
                    var sourceFilePath = _fileDiscoveryService.FindRippedFile(outputPath, track);
                    
                    if (!string.IsNullOrEmpty(sourceFilePath) && _fileDiscoveryService.VerifyFile(sourceFilePath))
                    {
                        var fileName = Path.GetFileName(sourceFilePath);
                        var destinationPath = Path.Combine(trashFolderPath, fileName);
                        
                        // Move file to trash folder
                        File.Move(sourceFilePath, destinationPath);
                        _logger.LogInformation($"Moved skipped track '{track.Name}' from '{sourceFilePath}' to '{destinationPath}'");
                    }
                    else
                    {
                        _logger.LogWarning($"Could not find ripped file for skipped track '{track.Name}' (ID: {track.Id})");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to move skipped track '{track.Name}' to _trash folder");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to create _trash folder or move skipped tracks");
        }
    }

}
