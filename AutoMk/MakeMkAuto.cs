using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMk.Interfaces;
using AutoMk.Models;
using AutoMk.Services;
using AutoMk.Utilities;
using Microsoft.Extensions.Logging;

namespace AutoMk;

public class MakeMkAuto : Microsoft.Extensions.Hosting.BackgroundService
{
    private readonly RipSettings _ripSettings;
    private readonly DriveWatcher _watcher;
    private readonly ILogger<MakeMkAuto> _logger;
    private readonly IMakeMkvService _makeMkvService;
    private readonly IMediaIdentificationService _mediaService;
    private readonly string _drivePath;
    private bool _isAsleep = false;
    private readonly IMediaMoverService _mediaMoverService;
    private readonly ManualModeService _manualModeService;
    private readonly IOmdbClient _omdbClient;
    private readonly IMediaNamingService _namingService;
    private readonly IMediaStateManager _stateManager;
    private readonly ISeriesProfileService _profileService;
    private readonly ConsoleInteractionService _consoleInteraction;
    private readonly IEnhancedOmdbService _enhancedOmdbService;
    private readonly IConsoleOutputService _consoleOutput;

    public MakeMkAuto(
        RipSettings ripSettings,
        DriveWatcher watcher,
        ILogger<MakeMkAuto> logger,
        IMakeMkvService makeMkvService,
        IMediaIdentificationService mediaService,
        IMediaMoverService mediaMoverService,
        ManualModeService manualModeService,
        IOmdbClient omdbClient,
        IMediaNamingService namingService,
        IMediaStateManager stateManager,
        ISeriesProfileService profileService,
        ConsoleInteractionService consoleInteraction,
        IEnhancedOmdbService enhancedOmdbService,
        IConsoleOutputService consoleOutput)
    {
        _watcher = watcher ?? throw new ArgumentNullException(nameof(watcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _makeMkvService = makeMkvService ?? throw new ArgumentNullException(nameof(makeMkvService));
        _mediaService = mediaService ?? throw new ArgumentNullException(nameof(mediaService));
        _ripSettings = ripSettings ?? throw new ArgumentNullException(nameof(ripSettings));
        _drivePath = _ripSettings.Output;
        _mediaMoverService = mediaMoverService;
        _manualModeService = manualModeService ?? throw new ArgumentNullException(nameof(manualModeService));
        _omdbClient = omdbClient ?? throw new ArgumentNullException(nameof(omdbClient));
        _namingService = namingService ?? throw new ArgumentNullException(nameof(namingService));
        _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        _consoleInteraction = consoleInteraction ?? throw new ArgumentNullException(nameof(consoleInteraction));
        _enhancedOmdbService = enhancedOmdbService ?? throw new ArgumentNullException(nameof(enhancedOmdbService));
        _consoleOutput = consoleOutput ?? throw new ArgumentNullException(nameof(consoleOutput));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pdrives = _watcher.PrintDrives();
        _logger.LogInformation("AutoMk Started");
        if (pdrives.Count == 0)
        {
            _consoleOutput.ShowError("No CD drives found");
            return;
        }
        Console.CancelKeyPress +=
            new ConsoleCancelEventHandler((a, b) =>
            {

                _logger.LogInformation("AutoMk Shutting Down");
                Environment.Exit(0);
            });
        var skipCheck = _ripSettings.ManualMode;
        while (!stoppingToken.IsCancellationRequested)
        {
            // check for files to move (skip in manual mode before ripping)
            // This now runs before checking if drives are ready, so files can be transferred even without a disc
            if (!skipCheck)
            {
                _logger.LogInformation("Checking for files to transfer/move...");
                try
                {
                    await _mediaMoverService.FindFiles(_ripSettings.Output);
                    _logger.LogInformation("Completed file transfer/move check");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during file transfer/move check");
                }
            }
            else
            {
                skipCheck = false;
            }

            if (!_watcher.AnyDriveReady())
            {
                if (!_isAsleep)
                {
                    _logger.LogInformation("Drives not ready, sleeping...");
                }
                _isAsleep = true;
                await Task.Delay(2000); // Reduced delay for faster wake detection
                continue; // Continue the loop instead of exiting
            }

            _isAsleep = false;
            _logger.LogInformation("Drive(s) detected as ready, checking for available drives with discs...");

            // Get available drives
            var drives = await GetAvailableDrivesAsync();
            if (!drives.Any())
            {
                _logger.LogInformation("No drives with discs found after drive ready detection, waiting...");
                await Task.Delay(3000); // Wait before checking again
                continue; // Continue the loop instead of exiting
            }

            _logger.LogInformation($"Found {drives.Count} drive(s) with discs ready for processing");

            // Process each drive
            foreach (var drive in drives)
            {
                _logger.LogInformation($"Attempting to process drive {drive.DriveLetter} with disc: {drive.CDName}");

                if (!_watcher.IsDriveReady(drive.DriveLetter))
                {
                    _logger.LogInformation($"Drive {drive.DriveLetter} not ready during individual check, skipping");
                    await Task.Delay(2000);
                    continue;
                }

                _logger.LogInformation($"Drive {drive.DriveLetter} confirmed ready, beginning processing...");
                await ProcessDriveAsync(drive);
                _logger.LogInformation($"Completed processing drive {drive.DriveLetter}");
            }
        }
    }



    private async Task<List<AkDriveInfo>> GetAvailableDrivesAsync()
    {
        var watch = Stopwatch.StartNew();

        try
        {
            _logger.LogDebug("Starting MakeMKV drive scan...");

            // Use the MakeMkvService wrapper to get available drives
            var drives = await _makeMkvService.GetAvailableDrivesAsync();

            watch.Stop();
            _logger.LogInformation($"Drive check took {watch.Elapsed.TotalSeconds:F2} seconds, found {drives.Count} drives");

            if (drives.Any())
            {
                foreach (var drive in drives)
                {
                    _logger.LogInformation($"Drive found: {drive.DriveLetter} - '{drive.CDName}' (ID: {drive.Id})");
                }
            }
            else
            {
                _logger.LogDebug("MakeMKV drive scan returned no drives with discs");
            }

            return drives;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting drive information");
            watch.Stop();
            return new List<AkDriveInfo>();
        }
    }

    private async Task ProcessDriveAsync(AkDriveInfo drive)
    {
        _logger.LogInformation($"ProcessDriveAsync started for drive {drive.DriveLetter} with disc '{drive.CDName}'");

        _consoleOutput.ShowDiscDetected(drive.CDName);
        _consoleOutput.ShowProcessingStarted(drive.CDName);

        try
        {
            _logger.LogInformation($"Getting disc information for drive {drive.DriveLetter}...");

            // Get disc information using the new wrapper
            var success = await _makeMkvService.GetDiscInfoAsync(drive);
            if (!success)
            {
                _logger.LogError($"Failed to get disc information for drive {drive.Id}");
                _consoleOutput.ShowError($"Failed to get disc information for drive {drive.Id}");
                return;
            }

            _logger.LogInformation($"Successfully got disc information for drive {drive.DriveLetter}, found {drive.Titles?.Count ?? 0} titles");

            if (_ripSettings.ManualMode)
            {
                await ProcessDriveManualModeAsync(drive);
            }
            else
            {
                await ProcessDriveAutomaticModeAsync(drive);
            }

            // Open drive when done
            _logger.LogInformation("Opening drive");

            if (_watcher.CanEjectDrive(drive.DriveLetter))
            {
                var ejectResult = _watcher.OpenDrive(drive.DriveLetter);
                if (!string.IsNullOrEmpty(ejectResult))
                {
                    _logger.LogWarning(ejectResult);
                }
                else
                {
                    _logger.LogInformation($"Successfully ejected drive {drive.DriveLetter}");
                }
            }
            else
            {
                _logger.LogWarning($"Drive {drive.DriveLetter} does not support ejection or is not accessible");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error processing drive {drive.Id}");
        }
    }

    private string GetOutputPath(AkDriveInfo drive)
    {
        string cdName = FindNextName(drive.CDName);
        string tempPath;

        if (_ripSettings.Flat)
        {
            // If flat structure, use a temp subdirectory
            tempPath = _drivePath;
        }
        else
        {
            // Use temp directory for initial rip
            tempPath = Path.Combine(_drivePath, "temp", cdName + "_" + DateTime.Now.Ticks);
        }

        // Ensure directory exists
        if (!Directory.Exists(tempPath))
        {
            Directory.CreateDirectory(tempPath);
            _logger.LogInformation($"Created temp output directory: {tempPath}");
        }

        return tempPath;
    }

    private void CleanupTempDirectory(string tempPath)
    {
        try
        {
            if (Directory.Exists(tempPath))
            {
                var remainingFiles = Directory.GetFiles(tempPath, "*", SearchOption.AllDirectories);
                if (!remainingFiles.Any())
                {
                    Directory.Delete(tempPath, true);
                    _logger.LogInformation($"Cleaned up temp directory: {tempPath}");
                }
                else
                {
                    _logger.LogInformation($"Temp directory contains {remainingFiles.Length} remaining files: {tempPath}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Error cleaning up temp directory: {tempPath}");
        }
    }

    private string FindNextName(string name)
    {
        if (_ripSettings.NameList == null)
        {
            return name;
        }

        foreach (var nameItem in _ripSettings.NameList)
        {
            if (nameItem.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var item in nameItem.List)
                {
                    var path = Path.Combine(_drivePath, item);
                    if (!Directory.Exists(path))
                    {
                        return item;
                    }
                }
                break;
            }
        }

        return name;
    }

    private async Task ProcessDriveAutomaticModeAsync(AkDriveInfo drive)
    {
        // Step 1: Pre-identify media before determining what to rip
        _logger.LogInformation($"Identifying media for disc: {drive.CDName}");
        var mediaInfo = await _mediaService.PreIdentifyMediaAsync(drive.CDName, drive.Titles.Values.ToList());

        if (mediaInfo == null)
        {
            _logger.LogInformation($"User skipped disc identification for: {drive.CDName}. Proceeding without pre-identification.");
        }
        else
        {
            _logger.LogInformation($"Pre-identified media: {mediaInfo.MediaData?.Title} ({mediaInfo.MediaData?.Type})");

            // Show media identification result in console
            if (!string.IsNullOrEmpty(mediaInfo.MediaData?.Title) && !string.IsNullOrEmpty(mediaInfo.MediaData?.Type))
            {
                _consoleOutput.ShowMediaIdentified(mediaInfo.MediaData.Title, mediaInfo.MediaData.Type);
            }

            // Step 2: For TV series, check for existing state/profile or create new one
            if (mediaInfo.MediaData?.Type?.Equals("series", StringComparison.OrdinalIgnoreCase) == true)
            {
                // First check if we have existing series state (from previous discs)
                var existingSeriesState = await _stateManager.GetExistingSeriesStateAsync(
                    mediaInfo.MediaData.Title!);

                // Also check for saved profile
                var profile = await _profileService.GetProfileAsync(mediaInfo.MediaData.Title!);

                if (existingSeriesState != null)
                {
                    _logger.LogInformation($"Found existing series state for {mediaInfo.MediaData.Title} with {existingSeriesState.ProcessedDiscs.Count} processed discs. Using existing configuration.");

                    // If we don't have a profile yet, create one from the existing state
                    if (profile == null)
                    {
                        profile = new SeriesProfile
                        {
                            SeriesTitle = mediaInfo.MediaData.Title!,
                            MinEpisodeSizeGB = existingSeriesState.MinEpisodeSizeGB,
                            MaxEpisodeSizeGB = existingSeriesState.MaxEpisodeSizeGB,
                            TrackSortingStrategy = existingSeriesState.TrackSortingStrategy ?? TrackSortingStrategy.ByTrackOrder,
                            DoubleEpisodeHandling = existingSeriesState.DoubleEpisodeHandling ?? DoubleEpisodeHandling.AlwaysAsk,
                            UseAutoIncrement = existingSeriesState.AutoIncrement
                        };

                        await _profileService.CreateOrUpdateProfileAsync(profile);
                        _logger.LogInformation($"Created profile from existing series state for {mediaInfo.MediaData.Title}");
                    }
                }
                else if (profile != null)
                {
                    _logger.LogInformation($"Using existing profile for series: {mediaInfo.MediaData.Title}");

                    // Update series state with profile settings if needed
                    var seriesState = await _stateManager.GetOrCreateSeriesStateAsync(
                        mediaInfo.MediaData.Title!);

                    seriesState.MinEpisodeSizeGB = profile.MinEpisodeSizeGB;
                    seriesState.MaxEpisodeSizeGB = profile.MaxEpisodeSizeGB;
                    seriesState.TrackSortingStrategy = profile.TrackSortingStrategy;
                    seriesState.DoubleEpisodeHandling = profile.DoubleEpisodeHandling;
                    seriesState.AutoIncrement = profile.UseAutoIncrement;

                    await _stateManager.SaveSeriesStateAsync(seriesState);
                }
                else
                {
                    // Truly new series - prompt for complete profile
                    _logger.LogInformation($"New TV series detected: {mediaInfo.MediaData.Title}. Prompting for complete profile.");
                    profile = _consoleInteraction.PromptForCompleteSeriesProfile(
                        mediaInfo.MediaData.Title!,
                        drive.CDName);

                    await _profileService.CreateOrUpdateProfileAsync(profile);

                    // Also update the series state with the profile settings
                    var seriesState = await _stateManager.GetOrCreateSeriesStateAsync(
                        mediaInfo.MediaData.Title!);

                    seriesState.MinEpisodeSizeGB = profile.MinEpisodeSizeGB;
                    seriesState.MaxEpisodeSizeGB = profile.MaxEpisodeSizeGB;
                    seriesState.TrackSortingStrategy = profile.TrackSortingStrategy;
                    seriesState.DoubleEpisodeHandling = profile.DoubleEpisodeHandling;
                    seriesState.AutoIncrement = profile.UseAutoIncrement;

                    await _stateManager.SaveSeriesStateAsync(seriesState);
                }

            }
        }

        // Get titles to rip based on media type
        List<AkTitle> titlesToRip;
        if (mediaInfo?.MediaData?.Type?.Equals("movie", StringComparison.OrdinalIgnoreCase) == true)
        {
            // For movies, select only the largest track
            var largestTitle = drive.Titles.Values
                .OrderByDescending(t => t.SizeInBytes)
                .FirstOrDefault();

            if (largestTitle != null)
            {
                titlesToRip = new List<AkTitle> { largestTitle };
                _logger.LogInformation($"Movie detected - selected largest track: {largestTitle.Name} ({largestTitle.SizeInGB:F2} GB)");
            }
            else
            {
                _consoleOutput.ShowWarning($"No tracks found on disc {drive.Id}");
                return;
            }
        }
        else
        {
            // For TV series or unknown media, use dynamic size filter
            if (_ripSettings.FilterBySize && _ripSettings.MinSizeGB > 0)
            {
                if (mediaInfo?.MediaData?.Type?.Equals("series", StringComparison.OrdinalIgnoreCase) == true)
                {
                    // Check if we have series-specific episode size range
                    double minSizeToUse = _ripSettings.MinSizeGB;
                    double maxSizeToUse = _ripSettings.MaxSizeGB;
                    bool useCustomSizeRange = false;

                    // Try to get existing series state to check for custom episode size range
                    var existingSeriesState = await _stateManager.GetExistingSeriesStateAsync(
                        mediaInfo.MediaData.Title!);

                    if (existingSeriesState?.MinEpisodeSizeGB.HasValue == true && existingSeriesState?.MaxEpisodeSizeGB.HasValue == true)
                    {
                        minSizeToUse = existingSeriesState.MinEpisodeSizeGB.Value;
                        maxSizeToUse = existingSeriesState.MaxEpisodeSizeGB.Value;
                        useCustomSizeRange = true;
                        _logger.LogInformation($"Using series-specific episode size range for {mediaInfo.MediaData.Title}: {minSizeToUse} - {maxSizeToUse} GB");
                    }

                    // Use appropriate filtering based on whether we have custom size range
                    if (useCustomSizeRange)
                    {
                        // Use fixed range filtering when custom sizes are specified
                        titlesToRip = _makeMkvService.FilterTitlesBySize(drive, minSizeToUse, maxSizeToUse);
                        if (!titlesToRip.Any())
                        {
                            _consoleOutput.ShowWarning($"No titles found in custom size range {minSizeToUse}-{maxSizeToUse} GB on drive {drive.Id}");
                            return;
                        }
                        _logger.LogInformation($"TV series - found {titlesToRip.Count} titles using custom size range: {minSizeToUse} - {maxSizeToUse} GB");
                    }
                    else
                    {
                        // Use dynamic filtering for TV series when no custom range is set
                        titlesToRip = _makeMkvService.FilterTitlesBySizeForTvSeries(drive, minSizeToUse);
                        if (!titlesToRip.Any())
                        {
                            _consoleOutput.ShowWarning($"No titles found meeting minimum size requirement of {minSizeToUse} GB on drive {drive.Id}");
                            return;
                        }
                        _logger.LogInformation($"TV series - found {titlesToRip.Count} titles using dynamic size filtering (min: {minSizeToUse} GB)");
                    }
                }
                else
                {
                    // Use fixed range for unknown media
                    titlesToRip = _makeMkvService.FilterTitlesBySize(drive, _ripSettings.MinSizeGB, _ripSettings.MaxSizeGB);
                    if (!titlesToRip.Any())
                    {
                        _consoleOutput.ShowWarning($"No titles found in size range {_ripSettings.MinSizeGB}-{_ripSettings.MaxSizeGB} GB on drive {drive.Id}");
                        return;
                    }
                    _logger.LogInformation($"Unknown media - found {titlesToRip.Count} titles in size range {_ripSettings.MinSizeGB}-{_ripSettings.MaxSizeGB} GB");
                }
            }
            else
            {
                titlesToRip = drive.Titles.Values.ToList();
            }
        }

        // Step 3: Pre-rip confirmation with all settings
        RipConfirmationResult confirmationResult = RipConfirmationResult.Proceed;
        bool skipConfirmationForSeries = false;

        // Check if we should skip confirmation for this series
        if (mediaInfo?.MediaData?.Type?.Equals("series", StringComparison.OrdinalIgnoreCase) == true)
        {
            var profile = await _profileService.GetProfileAsync(mediaInfo.MediaData.Title!);
            if (profile?.AlwaysSkipConfirmation == true)
            {
                skipConfirmationForSeries = true;
                _logger.LogInformation($"Skipping confirmation for series {mediaInfo.MediaData.Title} due to saved preference");
            }
        }

        if (!skipConfirmationForSeries)
        {
            var confirmation = new RipConfirmation
            {
                MediaTitle = mediaInfo?.MediaData?.Title ?? drive.CDName,
                MediaType = mediaInfo?.MediaData?.Type?.Equals("series", StringComparison.OrdinalIgnoreCase) == true ? "TV Series" : "Movie",
                TracksToRip = titlesToRip.Count,
                SelectedTracks = titlesToRip
            };

            if (mediaInfo?.MediaData?.Type?.Equals("series", StringComparison.OrdinalIgnoreCase) == true)
            {
                var seriesState = await _stateManager.GetExistingSeriesStateAsync(
                    mediaInfo.MediaData.Title!);

                confirmation.MinSizeGB = seriesState?.MinEpisodeSizeGB ?? _ripSettings.MinSizeGB;
                confirmation.MaxSizeGB = seriesState?.MaxEpisodeSizeGB ?? _ripSettings.MaxSizeGB;
                confirmation.SortingMethod = seriesState?.TrackSortingStrategy?.ToString() ?? "ByTrackOrder";
                confirmation.StartingPosition = $"S{seriesState?.CurrentSeason ?? 1:D2}E{seriesState?.NextEpisode ?? 1:D2}";
                confirmation.DoubleEpisodeHandling = seriesState?.DoubleEpisodeHandling?.ToString() ?? "AlwaysAsk";
            }
            else
            {
                var largestTrack = titlesToRip.OrderByDescending(t => t.SizeInBytes).FirstOrDefault();
                confirmation.MinSizeGB = largestTrack?.SizeInGB ?? 0;
                confirmation.MaxSizeGB = largestTrack?.SizeInGB ?? 0;
            }

            confirmationResult = _consoleInteraction.ConfirmRipSettings(confirmation);
        }
        switch (confirmationResult)
        {
            case RipConfirmationResult.Skip:
                _logger.LogInformation("User chose to skip disc");
                return;

            case RipConfirmationResult.ModifySettings:
                _logger.LogInformation("User chose to modify settings");

                // For TV series, allow modifying the series profile
                if (mediaInfo?.MediaData?.Type?.Equals("series", StringComparison.OrdinalIgnoreCase) == true)
                {
                    // Get the existing profile to modify
                    var existingProfile = await _profileService.GetProfileAsync(mediaInfo.MediaData.Title!);

                    SeriesProfile newProfile;
                    if (existingProfile != null)
                    {
                        // Use the modify method to update existing settings
                        newProfile = _consoleInteraction.PromptForModifySeriesProfile(existingProfile, drive.CDName);
                    }
                    else
                    {
                        // If no existing profile, create a new one from scratch
                        newProfile = _consoleInteraction.PromptForCompleteSeriesProfile(
                            mediaInfo.MediaData.Title!,
                            drive.CDName);
                    }

                    await _profileService.CreateOrUpdateProfileAsync(newProfile);

                    // Update the series state with the new profile settings
                    var seriesState = await _stateManager.GetOrCreateSeriesStateAsync(
                        mediaInfo.MediaData.Title!);

                    seriesState.MinEpisodeSizeGB = newProfile.MinEpisodeSizeGB;
                    seriesState.MaxEpisodeSizeGB = newProfile.MaxEpisodeSizeGB;
                    seriesState.TrackSortingStrategy = newProfile.TrackSortingStrategy;
                    seriesState.DoubleEpisodeHandling = newProfile.DoubleEpisodeHandling;
                    seriesState.AutoIncrement = newProfile.UseAutoIncrement;

                    await _stateManager.SaveSeriesStateAsync(seriesState);

                    _logger.LogInformation("Updated series settings, restarting processing with new configuration");

                    // Restart the processing with updated settings
                    await ProcessDriveAutomaticModeAsync(drive);
                    return;
                }
                else
                {
                    _logger.LogInformation("Settings modification not available for movies, skipping disc");
                    return;
                }

            case RipConfirmationResult.Proceed:
                _logger.LogInformation("User confirmed rip settings, proceeding");
                break;

            case RipConfirmationResult.ProceedAndDontAskAgain:
                _logger.LogInformation("User confirmed rip settings and chose to skip future confirmations for this series");

                // Update the series profile to skip confirmations in the future
                if (mediaInfo?.MediaData?.Type?.Equals("series", StringComparison.OrdinalIgnoreCase) == true)
                {
                    var profile = await _profileService.GetProfileAsync(mediaInfo.MediaData.Title!);
                    if (profile != null)
                    {
                        profile.AlwaysSkipConfirmation = true;
                        profile.LastModifiedDate = DateTime.Now;
                        await _profileService.CreateOrUpdateProfileAsync(profile);
                        _logger.LogInformation($"Updated series profile for {mediaInfo.MediaData.Title} to skip future confirmations");
                    }
                }
                break;
        }

        // Season data will be loaded lazily as needed during processing
        if (mediaInfo?.MediaData?.Type?.Equals("series", StringComparison.OrdinalIgnoreCase) == true)
        {
            try
            {
                var currentSeriesState = await _stateManager.GetExistingSeriesStateAsync(
                    mediaInfo.MediaData.Title!);

                // Season info will now be loaded lazily when needed
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error accessing series state - continuing");
            }
        }

        // Determine output path for ripping (temporary)
        string tempOutputPath = GetOutputPath(drive);

        // Rip the titles to temporary location
        _logger.LogInformation($"Ripping {titlesToRip.Count} titles from disc: {drive.CDName}");
        await _makeMkvService.RipTitlesAsync(drive, titlesToRip, tempOutputPath);

        // Now process and rename the ripped media
        _logger.LogInformation($"Processing and renaming ripped media from disc: {drive.CDName}");
        bool mediaProcessSuccess;

        // Pass the pre-identified media info to avoid duplicate lookups
        mediaProcessSuccess = await _mediaService.ProcessRippedMediaAsync(tempOutputPath, drive.CDName, titlesToRip, mediaInfo);

        if (!mediaProcessSuccess)
        {
            _consoleOutput.ShowWarning($"Media processing completed with some errors for disc: {drive.CDName}");
        }
        else
        {
            _consoleOutput.ShowSuccess($"Successfully processed and renamed media from disc: {drive.CDName}");
            _consoleOutput.ShowOrganizationCompleted(titlesToRip.Count);
        }

        // Clean up temp directory if it's empty
        CleanupTempDirectory(tempOutputPath);
    }

    private async Task ProcessDriveManualModeAsync(AkDriveInfo drive)
    {
        _logger.LogInformation($"MANUAL MODE - Processing disc: {drive.CDName}");

        // Step 1: Identify media with user confirmation
        var mediaData = await _manualModeService.IdentifyMediaWithConfirmationAsync(drive.CDName);
        if (mediaData == null)
        {
            _logger.LogInformation("User skipped media identification. Aborting disc processing.");
            return;
        }

        // Show media identification result in console
        if (!string.IsNullOrEmpty(mediaData.Title) && !string.IsNullOrEmpty(mediaData.Type))
        {
            _consoleOutput.ShowMediaIdentified(mediaData.Title, mediaData.Type);
        }

        // Step 2: Determine media type and get user choice if needed
        MediaType mediaType;
        if (mediaData.Type?.Equals("movie", StringComparison.OrdinalIgnoreCase) == true)
        {
            mediaType = MediaType.Movie;
        }
        else if (mediaData.Type?.Equals("series", StringComparison.OrdinalIgnoreCase) == true)
        {
            mediaType = MediaType.TvSeries;
        }
        else
        {
            // Unknown type - ask user
            mediaType = _manualModeService.PromptForMediaType();
        }

        // Step 3: Select tracks to rip
        var availableTitles = drive.Titles.Values.ToList();
        List<AkTitle> titlesToRip;

        if (mediaType == MediaType.Movie)
        {
            // For movies, suggest the largest track but let user choose
            var largestTitle = availableTitles.OrderByDescending(t => t.SizeInBytes).FirstOrDefault();
            Console.WriteLine($"Suggested track for movie (largest): {largestTitle?.Name} ({largestTitle?.SizeInGB:F2} GB)");
            titlesToRip = _manualModeService.SelectTracksToRip(availableTitles);
        }
        else
        {
            // For TV series, let user select tracks
            titlesToRip = _manualModeService.SelectTracksToRip(availableTitles);
        }

        if (!titlesToRip.Any())
        {
            _logger.LogInformation("No tracks selected. Aborting disc processing.");
            return;
        }

        // Step 4: Get episode mapping for TV series
        Dictionary<AkTitle, EpisodeInfo>? episodeMapping = null;
        if (mediaType == MediaType.TvSeries)
        {
            episodeMapping = _manualModeService.MapTracksToEpisodes(titlesToRip, mediaData.Title ?? "Unknown Series");
        }

        // Step 5: Determine output path for ripping
        string tempOutputPath = GetOutputPath(drive);

        // Step 6: Rip the selected titles
        _logger.LogInformation($"MANUAL MODE - Ripping {titlesToRip.Count} selected titles from disc: {drive.CDName}");
        await _makeMkvService.RipTitlesAsync(drive, titlesToRip, tempOutputPath);

        // Step 7: Process and rename files based on manual selections
        _logger.LogInformation($"MANUAL MODE - Processing and renaming ripped media from disc: {drive.CDName}");
        bool success;

        if (mediaType == MediaType.Movie)
        {
            var movieInfo = new MovieInfo
            {
                Title = mediaData.Title,
                Year = mediaData.Year,
                Type = mediaData.Type,
                ImdbID = mediaData.ImdbID
            };
            success = await ProcessManualMovieAsync(tempOutputPath, drive.CDName, titlesToRip, movieInfo);
        }
        else
        {
            var seriesInfo = new SeriesInfo
            {
                Title = mediaData.Title,
                Year = mediaData.Year,
                Type = mediaData.Type,
                ImdbID = mediaData.ImdbID
            };
            success = await ProcessManualTvSeriesAsync(tempOutputPath, drive.CDName, titlesToRip, seriesInfo, episodeMapping!);
        }

        if (!success)
        {
            _consoleOutput.ShowWarning($"MANUAL MODE - Media processing completed with some errors for disc: {drive.CDName}");
        }
        else
        {
            _consoleOutput.ShowSuccess($"MANUAL MODE - Successfully processed and renamed media from disc: {drive.CDName}");
            _consoleOutput.ShowOrganizationCompleted(titlesToRip.Count);
        }

        // Clean up temp directory if it's empty
        CleanupTempDirectory(tempOutputPath);
    }

    private async Task<bool> ProcessManualMovieAsync(string outputPath, string discName, List<AkTitle> rippedTracks, MovieInfo movieInfo)
    {
        // This will reuse the existing movie processing logic from MediaIdentificationService
        // For now, create a PreIdentifiedMedia object and use the existing processing
        var preIdentifiedMedia = new PreIdentifiedMedia
        {
            MediaData = ModelConverter.ToMediaIdentity(movieInfo),
            IsFromCache = false,
            IdentificationSource = "manual"
        };

        return await _mediaService.ProcessRippedMediaAsync(outputPath, discName, rippedTracks, preIdentifiedMedia);
    }

    private async Task<bool> ProcessManualTvSeriesAsync(string outputPath, string discName, List<AkTitle> rippedTracks, SeriesInfo seriesInfo, Dictionary<AkTitle, EpisodeInfo> episodeMapping)
    {
        // This is a simplified version that doesn't use state management
        // Each track is processed according to the manual episode mapping

        var success = true;

        foreach (var kvp in episodeMapping)
        {
            var track = kvp.Key;
            var episodeInfo = kvp.Value;

            try
            {
                var originalFile = FindRippedFile(outputPath, track);
                if (originalFile == null)
                {
                    _logger.LogWarning($"Could not find ripped file for track: {track.Id}");
                    success = false;
                    continue;
                }

                // Try to get episode details from enhanced OMDB service (uses caching)
                CachedEpisodeInfo? episodeData = null;
                try
                {
                    episodeData = await _enhancedOmdbService.GetEpisodeInfoAsync(
                        seriesInfo.Title!,
                        episodeInfo.Season,
                        episodeInfo.Episode);
                }
                catch (Exception omdbEx)
                {
                    _logger.LogWarning(omdbEx, $"Could not fetch episode details from OMDB for S{episodeInfo.Season:D2}E{episodeInfo.Episode:D2}");
                }

                // Generate episode filename using the naming service
                var newFileName = _namingService.GenerateEpisodeFileName(
                    seriesInfo.Title!,
                    episodeInfo.Season,
                    episodeInfo.Episode,
                    episodeData?.Title,
                    Path.GetExtension(originalFile));

                var newFilePath = Path.Combine(
                    _namingService.GetSeriesDirectory(outputPath, seriesInfo.Title!),
                    _namingService.GetSeasonDirectory(episodeInfo.Season),
                    newFileName);

                // Ensure directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(newFilePath)!);

                // Move the file
                File.Move(originalFile, newFilePath);
                _logger.LogInformation($"MANUAL MODE - Renamed: {Path.GetFileName(originalFile)} -> {newFileName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing manual episode S{episodeInfo.Season:D2}E{episodeInfo.Episode:D2} for {seriesInfo.Title}");
                success = false;
            }
        }

        return success;
    }

    private string? FindRippedFile(string outputPath, AkTitle track)
    {
        // First, try to find the file using the actual track name from MakeMKV
        if (!string.IsNullOrEmpty(track.Name))
        {
            // track.Name should contain the actual filename like "Frasier_S8_D1_BD_t00.mkv"
            var exactMatch = Path.Combine(outputPath, track.Name.Trim('"', ' '));
            if (File.Exists(exactMatch))
            {
                _logger.LogDebug($"Found file using exact track name: {track.Name}");
                return exactMatch;
            }
        }

        _logger.LogWarning("Missing track file");
        return null;
    }

}
