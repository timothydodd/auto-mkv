using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMk.Interfaces;
using AutoMk.Models;
using AutoMk.Utilities;
using Microsoft.Extensions.Logging;

namespace AutoMk.Services;

/// <summary>
/// Service for handling TV series configuration and profile management UI interactions
/// </summary>
public class SeriesConfigurationService : ISeriesConfigurationService
{
    private readonly ILogger<SeriesConfigurationService> _logger;
    private readonly IConsolePromptService _promptService;
    private readonly IPatternLearningService _patternLearningService;
    private bool _acceptAllSuggestionsForDisc = false;

    public SeriesConfigurationService(ILogger<SeriesConfigurationService> logger, IConsolePromptService promptService, IPatternLearningService patternLearningService)
    {
        _logger = ValidationHelper.ValidateNotNull(logger);
        _promptService = ValidationHelper.ValidateNotNull(promptService);
        _patternLearningService = ValidationHelper.ValidateNotNull(patternLearningService);
    }

    public (int season, int episode) PromptForStartingSeasonAndEpisode(string seriesTitle, string discName)
    {
        _promptService.DisplayHeader("AUTOMATIC MODE - Season/Episode Information Needed");
        
        Console.WriteLine($"Series: {seriesTitle}");
        Console.WriteLine($"Disc: {discName}");
        Console.WriteLine();
        Console.WriteLine("This disc hasn't been processed before and the season/episode");
        Console.WriteLine("information cannot be determined automatically.");
        Console.WriteLine();

        var seasonResult = _promptService.NumberPrompt(new NumberPromptOptions
        {
            Question = "What season does this disc belong to?",
            Required = true,
            PromptText = "Season",
            MinValue = 1,
            MaxValue = 50
        });

        int season = seasonResult.Success ? seasonResult.Value : 1;

        var episodeResult = _promptService.NumberPrompt(new NumberPromptOptions
        {
            Question = $"What episode number should we start with for Season {season}?",
            Required = true,
            PromptText = "Episode",
            MinValue = 1,
            MaxValue = 100
        });

        int episode = episodeResult.Success ? episodeResult.Value : 1;

        Console.WriteLine($"Starting with Season {season}, Episode {episode}");
        
        _logger.LogInformation($"User specified starting point for {seriesTitle}: S{season:D2}E{episode:D2}");
        
        return (season, episode);
    }

    public (double? minSize, double? maxSize) PromptForEpisodeSizeRange(string seriesTitle)
    {
        _promptService.DisplayHeader("NEW TV SERIES DETECTED - Episode Size Filter");
        Console.WriteLine($"Series: {seriesTitle}");
        Console.WriteLine();
        Console.WriteLine("This is a new TV series. To improve episode filtering, you can specify");
        Console.WriteLine("minimum and maximum file sizes for episodes in this series. This helps");
        Console.WriteLine("skip extras, intros, and other unwanted files.");
        Console.WriteLine();

        var result = _promptService.SelectPrompt<bool>(new SelectPromptOptions
        {
            Question = "Would you like to set custom episode size filtering?",
            Choices = new List<PromptChoice>
            {
                new("custom", "Set custom episode size range (min and max in GB)", true),
                new("default", "Use default filtering (current global settings)", false)
            }
        });

        if (!result.Success || result.Cancelled || !result.Value)
        {
            _logger.LogInformation($"User chose to use default filtering for series: {seriesTitle}");
            return (null, null);
        }

        return PromptForSizeRange();
    }

    public (int? minChapters, int? maxChapters) PromptForEpisodeChapterRange(string seriesTitle)
    {
        _promptService.DisplayHeader("NEW TV SERIES DETECTED - Episode Chapter Filter");
        Console.WriteLine($"Series: {seriesTitle}");
        Console.WriteLine();
        Console.WriteLine("This is a new TV series. To improve episode filtering, you can specify");
        Console.WriteLine("minimum and maximum chapter counts for episodes in this series. This helps");
        Console.WriteLine("skip extras, intros, and other unwanted files based on chapter structure.");
        Console.WriteLine();

        var result = _promptService.SelectPrompt<bool>(new SelectPromptOptions
        {
            Question = "Would you like to set custom episode chapter filtering?",
            Choices = new List<PromptChoice>
            {
                new("custom", "Set custom episode chapter range (min and max chapters)", true),
                new("default", "Use default filtering (current global settings)", false)
            }
        });

        if (!result.Success || result.Cancelled || !result.Value)
        {
            _logger.LogInformation($"User chose to use default chapter filtering for series: {seriesTitle}");
            return (null, null);
        }

        return PromptForChapterRange();
    }

    public TrackSortingStrategy PromptForTrackSortingStrategy(string seriesTitle)
    {
        _promptService.DisplayHeader($"Track Sorting Strategy for TV Series: {seriesTitle}");
        Console.WriteLine("For TV series, tracks need to be sorted to determine episode order.");
        Console.WriteLine("Choose how you want tracks to be sorted:");
        Console.WriteLine();
        Console.WriteLine("Track Order is usually correct, but some discs may have episodes in wrong order.");
        Console.WriteLine("MPLS File Name sorting can help when episodes are numbered correctly in the files.");
        Console.WriteLine("User Confirmed allows you to verify/correct each episode assignment manually.");
        Console.WriteLine();

        var result = _promptService.SelectPrompt<TrackSortingStrategy>(new SelectPromptOptions
        {
            Question = "Choose track sorting method:",
            Choices = new List<PromptChoice>
            {
                new("track_order", "By Track Order (default) - Use MakeMKV's track numbering (Title #0, #1, #2...)", TrackSortingStrategy.ByTrackOrder),
                new("mpls_filename", "By MPLS File Name - Sort by source MPLS file names (00042.mpls, 00043.mpls...)", TrackSortingStrategy.ByMplsFileName),
                new("user_confirmed", "User Confirmed - Sort by track order but confirm each episode with user", TrackSortingStrategy.UserConfirmed)
            }
        });

        if (!result.Success || result.Cancelled)
        {
            _logger.LogInformation("User cancelled track sorting selection, defaulting to ByTrackOrder for series: {SeriesTitle}", seriesTitle);
            return TrackSortingStrategy.ByTrackOrder;
        }

        _logger.LogInformation("User selected {Strategy} sorting for series: {SeriesTitle}", result.Value, seriesTitle);
        return result.Value;
    }

    public (bool treatAsDouble, DoubleEpisodeHandling? savePreference) PromptForDoubleEpisodeHandling(
        string seriesTitle, 
        string trackName, 
        double trackLengthSeconds, 
        double minEpisodeLengthSeconds)
    {
        double ratio = trackLengthSeconds / minEpisodeLengthSeconds;
        
        _promptService.DisplayHeader("POSSIBLE DOUBLE EPISODE DETECTED");
        Console.WriteLine($"Series: {seriesTitle}");
        Console.WriteLine($"Track: {trackName}");
        Console.WriteLine($"Track Length: {trackLengthSeconds / 60:F1} minutes");
        Console.WriteLine($"Minimum Episode Length: {minEpisodeLengthSeconds / 60:F1} minutes");
        Console.WriteLine($"Length Ratio: {ratio:F2}x longer than shortest episode");
        Console.WriteLine();
        Console.WriteLine("This track appears to be significantly longer than the shortest episode.");
        Console.WriteLine("It might contain two episodes combined into one file.");
        Console.WriteLine();

        var result = _promptService.SelectPrompt(new SelectPromptOptions
        {
            Question = "How should this be handled for episode numbering?",
            Choices = new List<PromptChoice>
            {
                new("double_once", "Treat as TWO episodes (increment episode count by 2)"),
                new("single_once", "Treat as ONE episode (normal increment)"),
                new("double_always", "ALWAYS treat long episodes as DOUBLE for this series (save preference)"),
                new("single_always", "NEVER treat long episodes as single for this series (save preference)")
            }
        });

        if (!result.Success || result.Cancelled)
        {
            _logger.LogInformation($"User cancelled double episode choice for {trackName}, defaulting to single episode");
            return (false, null);
        }

        switch (result.Value)
        {
            case "double_once":
                _logger.LogInformation($"User chose to treat {trackName} as double episode");
                return (true, null);
                
            case "single_once":
                _logger.LogInformation($"User chose to treat {trackName} as single episode");
                return (false, null);
                
            case "double_always":
                _logger.LogInformation($"User chose to always treat long episodes as double for series: {seriesTitle}");
                return (true, DoubleEpisodeHandling.AlwaysDouble);
                
            case "single_always":
                _logger.LogInformation($"User chose to always treat long episodes as single for series: {seriesTitle}");
                return (false, DoubleEpisodeHandling.AlwaysSingle);
                
            default:
                _logger.LogWarning($"Unexpected choice value: {result.Value}, defaulting to single episode");
                return (false, null);
        }
    }

    public SeriesProfile PromptForCompleteSeriesProfile(string seriesTitle, string discName)
    {
        _promptService.DisplayHeader("NEW TV SERIES CONFIGURATION");
        Console.WriteLine($"Series: {seriesTitle}");
        Console.WriteLine($"Disc: {discName}");
        Console.WriteLine();
        Console.WriteLine("This is a new TV series. Let's configure all settings upfront to minimize");
        Console.WriteLine("interruptions during the ripping process.");
        Console.WriteLine();

        var profile = new SeriesProfile
        {
            SeriesTitle = seriesTitle
        };

        // 1. Episode Size Range
        Console.WriteLine("STEP 1/6: Episode Size Filtering");
        Console.WriteLine("---------------------------------");
        var (minSize, maxSize) = PromptForEpisodeSizeRange(seriesTitle);
        profile.MinEpisodeSizeGB = minSize;
        profile.MaxEpisodeSizeGB = maxSize;
        Console.WriteLine();

        // 2. Episode Chapter Range
        Console.WriteLine("STEP 2/6: Episode Chapter Filtering");
        Console.WriteLine("------------------------------------");
        var (minChapters, maxChapters) = PromptForEpisodeChapterRange(seriesTitle);
        profile.MinEpisodeChapters = minChapters;
        profile.MaxEpisodeChapters = maxChapters;
        Console.WriteLine();

        // 3. Track Sorting Strategy
        Console.WriteLine("STEP 3/6: Track Sorting Method");
        Console.WriteLine("-------------------------------");
        profile.TrackSortingStrategy = PromptForTrackSortingStrategy(seriesTitle);
        Console.WriteLine();

        // 4. Double Episode Handling
        Console.WriteLine("STEP 4/6: Double Episode Detection");
        Console.WriteLine("-----------------------------------");
        Console.WriteLine("Some discs combine two episodes into a single file.");
        Console.WriteLine("How should these be handled?");
        Console.WriteLine();
        Console.WriteLine("1. Always ask me for each long episode (recommended)");
        Console.WriteLine("2. Always treat as single episodes");
        Console.WriteLine("3. Always treat long files as double episodes");
        Console.WriteLine();

        var doubleEpisodeResult = _promptService.SelectPrompt<DoubleEpisodeHandling>(new SelectPromptOptions
        {
            Question = "How should double episodes be handled?",
            Choices = new List<PromptChoice>
            {
                new("ask", "Always ask me for each long episode (recommended)", DoubleEpisodeHandling.AlwaysAsk),
                new("single", "Always treat as single episodes", DoubleEpisodeHandling.AlwaysSingle),
                new("double", "Always treat long files as double episodes", DoubleEpisodeHandling.AlwaysDouble)
            }
        });

        if (doubleEpisodeResult.Success && !doubleEpisodeResult.Cancelled)
        {
            profile.DoubleEpisodeHandling = doubleEpisodeResult.Value;
            _logger.LogInformation($"Set double episode handling to {doubleEpisodeResult.Value} for {seriesTitle}");
        }
        else
        {
            profile.DoubleEpisodeHandling = DoubleEpisodeHandling.AlwaysAsk;
            _logger.LogInformation($"User cancelled double episode choice, defaulting to Always Ask for {seriesTitle}");
        }
        Console.WriteLine();

        // 5. Starting Season/Episode (if not clear from disc name)
        Console.WriteLine("STEP 5/6: Starting Position");
        Console.WriteLine("----------------------------");
        if (!discName.Contains("S", StringComparison.OrdinalIgnoreCase) || 
            !discName.Contains("D", StringComparison.OrdinalIgnoreCase))
        {
            var (season, episode) = PromptForStartingSeasonAndEpisode(seriesTitle, discName);
            profile.DefaultStartingSeason = season;
            profile.DefaultStartingEpisode = episode;
        }
        else
        {
            Console.WriteLine("Season/Episode information will be extracted from disc name.");
        }
        Console.WriteLine();

        // 6. Auto-increment mode
        Console.WriteLine("STEP 6/6: Multi-Disc Handling");
        Console.WriteLine("------------------------------");
        Console.WriteLine("When processing multiple discs with similar names, should episode");
        Console.WriteLine("numbers automatically continue from where the previous disc ended?");
        Console.WriteLine();
        Console.WriteLine("1. Yes - Enable auto-increment mode");
        Console.WriteLine("2. No - Always check disc processing history");
        Console.WriteLine();

        var autoIncrementResult = _promptService.SelectPrompt<bool>(new SelectPromptOptions
        {
            Question = "Enable auto-increment mode for multi-disc handling?",
            Choices = new List<PromptChoice>
            {
                new("yes", "Yes - Enable auto-increment mode", true),
                new("no", "No - Always check disc processing history", false)
            }
        });

        if (autoIncrementResult.Success && !autoIncrementResult.Cancelled)
        {
            profile.UseAutoIncrement = autoIncrementResult.Value;
            _logger.LogInformation($"{(autoIncrementResult.Value ? "Enabled" : "Disabled")} auto-increment mode for {seriesTitle}");
        }
        else
        {
            profile.UseAutoIncrement = false;
            _logger.LogInformation($"User cancelled auto-increment choice, defaulting to disabled for {seriesTitle}");
        }

        Console.WriteLine();
        _promptService.DisplayHeader($"Series configuration complete! These settings will be used for all future discs from {seriesTitle}.");

        return profile;
    }

    public SeriesProfile PromptForModifySeriesProfile(SeriesProfile existingProfile, string discName)
    {
        _promptService.DisplayHeader("MODIFY TV SERIES CONFIGURATION");
        Console.WriteLine($"Series: {existingProfile.SeriesTitle}");
        Console.WriteLine($"Disc: {discName}");
        Console.WriteLine();
        Console.WriteLine("You can modify individual settings or keep the existing values.");
        Console.WriteLine("For each setting, you'll see the current value and can choose to keep it or change it.");
        Console.WriteLine();

        var modifiedProfile = new SeriesProfile
        {
            SeriesTitle = existingProfile.SeriesTitle,
            CreatedDate = existingProfile.CreatedDate,
            LastModifiedDate = DateTime.Now
        };

        // 1. Episode Size Range
        Console.WriteLine("SETTING 1/5: Episode Size Filtering");
        Console.WriteLine("------------------------------------");
        if (existingProfile.MinEpisodeSizeGB.HasValue && existingProfile.MaxEpisodeSizeGB.HasValue)
        {
            Console.WriteLine($"Current: Custom range {existingProfile.MinEpisodeSizeGB:F1} - {existingProfile.MaxEpisodeSizeGB:F1} GB");
        }
        else
        {
            Console.WriteLine("Current: Using default filtering (global settings)");
        }
        Console.WriteLine();
        Console.WriteLine("1. Keep current episode size settings");
        Console.WriteLine("2. Change episode size settings");
        Console.WriteLine();

        var sizeSettingsResult = _promptService.SelectPrompt<bool>(new SelectPromptOptions
        {
            Question = "Keep current episode size settings?",
            Choices = new List<PromptChoice>
            {
                new("keep", "Keep current episode size settings", true),
                new("change", "Change episode size settings", false)
            }
        });

        if (!sizeSettingsResult.Success || sizeSettingsResult.Cancelled || sizeSettingsResult.Value)
        {
            modifiedProfile.MinEpisodeSizeGB = existingProfile.MinEpisodeSizeGB;
            modifiedProfile.MaxEpisodeSizeGB = existingProfile.MaxEpisodeSizeGB;
            _logger.LogInformation($"Kept existing episode size settings for {existingProfile.SeriesTitle}");
        }
        else
        {
            var (minSize, maxSize) = PromptForEpisodeSizeRange(existingProfile.SeriesTitle);
            modifiedProfile.MinEpisodeSizeGB = minSize;
            modifiedProfile.MaxEpisodeSizeGB = maxSize;
        }
        Console.WriteLine();

        // 2. Track Sorting Strategy
        Console.WriteLine("SETTING 2/5: Track Sorting Method");
        Console.WriteLine("----------------------------------");
        Console.WriteLine($"Current: {existingProfile.TrackSortingStrategy}");
        Console.WriteLine();
        Console.WriteLine("1. Keep current track sorting method");
        Console.WriteLine("2. Change track sorting method");
        Console.WriteLine();

        var trackSortingResult = _promptService.SelectPrompt<bool>(new SelectPromptOptions
        {
            Question = "Keep current track sorting method?",
            Choices = new List<PromptChoice>
            {
                new("keep", "Keep current track sorting method", true),
                new("change", "Change track sorting method", false)
            }
        });

        if (!trackSortingResult.Success || trackSortingResult.Cancelled || trackSortingResult.Value)
        {
            modifiedProfile.TrackSortingStrategy = existingProfile.TrackSortingStrategy;
            _logger.LogInformation($"Kept existing track sorting strategy for {existingProfile.SeriesTitle}");
        }
        else
        {
            modifiedProfile.TrackSortingStrategy = PromptForTrackSortingStrategy(existingProfile.SeriesTitle);
        }
        Console.WriteLine();

        // 3. Double Episode Handling
        Console.WriteLine("SETTING 3/5: Double Episode Detection");
        Console.WriteLine("--------------------------------------");
        Console.WriteLine($"Current: {existingProfile.DoubleEpisodeHandling}");
        Console.WriteLine();
        Console.WriteLine("1. Keep current double episode handling");
        Console.WriteLine("2. Change double episode handling");
        Console.WriteLine();

        var doubleEpisodeKeepResult = _promptService.SelectPrompt<bool>(new SelectPromptOptions
        {
            Question = "Keep current double episode handling?",
            Choices = new List<PromptChoice>
            {
                new("keep", "Keep current double episode handling", true),
                new("change", "Change double episode handling", false)
            }
        });

        if (!doubleEpisodeKeepResult.Success || doubleEpisodeKeepResult.Cancelled || doubleEpisodeKeepResult.Value)
        {
            modifiedProfile.DoubleEpisodeHandling = existingProfile.DoubleEpisodeHandling;
            _logger.LogInformation($"Kept existing double episode handling for {existingProfile.SeriesTitle}");
        }
        else
        {
            Console.WriteLine();
            var newDoubleHandlingResult = _promptService.SelectPrompt<DoubleEpisodeHandling>(new SelectPromptOptions
            {
                Question = "How should long episodes be handled?",
                Choices = new List<PromptChoice>
                {
                    new("ask", "Always ask me for each long episode (recommended)", DoubleEpisodeHandling.AlwaysAsk),
                    new("single", "Always treat as single episodes", DoubleEpisodeHandling.AlwaysSingle),
                    new("double", "Always treat long files as double episodes", DoubleEpisodeHandling.AlwaysDouble)
                }
            });

            if (newDoubleHandlingResult.Success && !newDoubleHandlingResult.Cancelled)
            {
                modifiedProfile.DoubleEpisodeHandling = newDoubleHandlingResult.Value;
                _logger.LogInformation($"Set double episode handling to {newDoubleHandlingResult.Value} for {existingProfile.SeriesTitle}");
            }
            else
            {
                modifiedProfile.DoubleEpisodeHandling = existingProfile.DoubleEpisodeHandling;
                _logger.LogInformation($"User cancelled double episode change, keeping existing setting for {existingProfile.SeriesTitle}");
            }
        }
        Console.WriteLine();

        // 4. Starting Season/Episode (if not clear from disc name)
        Console.WriteLine("SETTING 4/5: Starting Position");
        Console.WriteLine("-------------------------------");
        if (existingProfile.DefaultStartingSeason.HasValue && existingProfile.DefaultStartingEpisode.HasValue)
        {
            Console.WriteLine($"Current: Default starting position S{existingProfile.DefaultStartingSeason:D2}E{existingProfile.DefaultStartingEpisode:D2}");
        }
        else
        {
            Console.WriteLine("Current: Extract from disc name automatically");
        }
        Console.WriteLine();
        Console.WriteLine("1. Keep current starting position settings");
        Console.WriteLine("2. Change starting position settings");
        Console.WriteLine();

        var startingPositionResult = _promptService.SelectPrompt<bool>(new SelectPromptOptions
        {
            Question = "Keep current starting position settings?",
            Choices = new List<PromptChoice>
            {
                new("keep", "Keep current starting position settings", true),
                new("change", "Change starting position settings", false)
            }
        });

        if (!startingPositionResult.Success || startingPositionResult.Cancelled || startingPositionResult.Value)
        {
            modifiedProfile.DefaultStartingSeason = existingProfile.DefaultStartingSeason;
            modifiedProfile.DefaultStartingEpisode = existingProfile.DefaultStartingEpisode;
            _logger.LogInformation($"Kept existing starting position settings for {existingProfile.SeriesTitle}");
        }
        else
        {
            if (!discName.Contains("S", StringComparison.OrdinalIgnoreCase) || 
                !discName.Contains("D", StringComparison.OrdinalIgnoreCase))
            {
                var (season, episode) = PromptForStartingSeasonAndEpisode(existingProfile.SeriesTitle, discName);
                modifiedProfile.DefaultStartingSeason = season;
                modifiedProfile.DefaultStartingEpisode = episode;
            }
            else
            {
                Console.WriteLine("Season/Episode information will be extracted from disc name.");
                modifiedProfile.DefaultStartingSeason = null;
                modifiedProfile.DefaultStartingEpisode = null;
            }
        }
        Console.WriteLine();

        // 5. Auto-increment mode
        Console.WriteLine("SETTING 5/5: Multi-Disc Handling");
        Console.WriteLine("---------------------------------");
        Console.WriteLine($"Current: Auto-increment {(existingProfile.UseAutoIncrement ? "Enabled" : "Disabled")}");
        Console.WriteLine();
        Console.WriteLine("1. Keep current auto-increment setting");
        Console.WriteLine("2. Change auto-increment setting");
        Console.WriteLine();

        var autoIncrementKeepResult = _promptService.SelectPrompt<bool>(new SelectPromptOptions
        {
            Question = "Keep current auto-increment setting?",
            Choices = new List<PromptChoice>
            {
                new("keep", "Keep current auto-increment setting", true),
                new("change", "Change auto-increment setting", false)
            }
        });

        if (!autoIncrementKeepResult.Success || autoIncrementKeepResult.Cancelled || autoIncrementKeepResult.Value)
        {
            modifiedProfile.UseAutoIncrement = existingProfile.UseAutoIncrement;
            _logger.LogInformation($"Kept existing auto-increment setting for {existingProfile.SeriesTitle}");
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("When processing multiple discs with similar names, should episode");
            Console.WriteLine("numbers automatically continue from where the previous disc ended?");
            Console.WriteLine();

            var newAutoIncrementResult = _promptService.SelectPrompt<bool>(new SelectPromptOptions
            {
                Question = "Enable auto-increment mode?",
                Choices = new List<PromptChoice>
                {
                    new("yes", "Yes - Enable auto-increment mode", true),
                    new("no", "No - Always check disc processing history", false)
                }
            });

            if (newAutoIncrementResult.Success && !newAutoIncrementResult.Cancelled)
            {
                modifiedProfile.UseAutoIncrement = newAutoIncrementResult.Value;
                _logger.LogInformation($"{(newAutoIncrementResult.Value ? "Enabled" : "Disabled")} auto-increment mode for {existingProfile.SeriesTitle}");
            }
            else
            {
                modifiedProfile.UseAutoIncrement = existingProfile.UseAutoIncrement;
                _logger.LogInformation($"User cancelled auto-increment change, keeping existing setting for {existingProfile.SeriesTitle}");
            }
        }

        Console.WriteLine();
        _promptService.DisplayHeader($"Settings modification complete! Updated settings will be used for this disc and saved for future discs from {existingProfile.SeriesTitle}.");

        return modifiedProfile;
    }

    public async Task<int?> ConfirmOrSelectEpisodeAsync(string seriesTitle, int season, int suggestedEpisode, string episodeTitle, string trackName, List<int> availableEpisodes, IEnhancedOmdbService enhancedOmdbService, List<AkTitle>? allTracks = null)
    {
        // For backward compatibility - this version doesn't use pattern learning
        return await ConfirmOrSelectEpisodeWithPatternLearningAsync(seriesTitle, season, suggestedEpisode, episodeTitle, trackName, availableEpisodes, enhancedOmdbService, allTracks, "", "", -1);
    }

    /// <summary>
    /// Enhanced version with pattern learning support for UserConfirmed sorting strategy
    /// </summary>
    public async Task<int?> ConfirmOrSelectEpisodeWithPatternLearningAsync(string seriesTitle, int season, int suggestedEpisode, string episodeTitle, string trackName, List<int> availableEpisodes, IEnhancedOmdbService enhancedOmdbService, List<AkTitle>? allTracks, string discName, string trackId, int trackPosition)
    {
        // Check if we have learned patterns for UserConfirmed strategy only
        var hasPatterns = !string.IsNullOrEmpty(discName) && trackPosition >= 0 && 
                         _patternLearningService.HasLearnedPatterns(seriesTitle, season, discName);
        
        var (patternSuggestion, confidence) = hasPatterns && !string.IsNullOrEmpty(discName) && trackPosition >= 0
            ? _patternLearningService.GetSuggestedEpisode(seriesTitle, season, discName, trackPosition, suggestedEpisode)
            : (suggestedEpisode, 0.0);

        // Use pattern suggestion if confidence is high enough
        var finalSuggestion = confidence > 0.7 ? patternSuggestion : suggestedEpisode;
        
        // If user has chosen to accept all suggestions for this disc, skip confirmation
        if (_acceptAllSuggestionsForDisc)
        {
            _logger.LogInformation($"Auto-accepting episode {finalSuggestion} for track {trackName} (disc-wide auto-accept enabled)");
            
            // Record the selection for pattern learning (only for UserConfirmed strategy)
            if (!string.IsNullOrEmpty(discName) && !string.IsNullOrEmpty(trackId) && trackPosition >= 0)
            {
                _patternLearningService.RecordSelection(seriesTitle, season, discName, trackPosition, 
                                                       trackId, trackName, finalSuggestion, finalSuggestion, true);
            }
            
            return finalSuggestion;
        }
        var isPatternBased = confidence > 0.7 && patternSuggestion != suggestedEpisode;

        _promptService.DisplayHeader("EPISODE CONFIRMATION");
        Console.WriteLine($"Series: {seriesTitle}");
        Console.WriteLine($"Season: {season}");
        Console.WriteLine($"Track: {trackName}");
        Console.WriteLine();
        
        if (isPatternBased)
        {
            Console.WriteLine($"Suggested Episode: {finalSuggestion} (based on learned UserConfirmed pattern, confidence: {confidence:P0})");
        }
        else
        {
            Console.WriteLine($"Suggested Episode: {finalSuggestion}");
        }
        
        if (!string.IsNullOrEmpty(episodeTitle))
        {
            Console.WriteLine($"Episode Title: {episodeTitle}");
        }
        
        if (hasPatterns && !isPatternBased && confidence <= 0.7)
        {
            Console.WriteLine($"Note: UserConfirmed pattern available but confidence is low ({confidence:P0})");
        }
        else if (hasPatterns && !isPatternBased && confidence > 0.7)
        {
            Console.WriteLine($"Note: UserConfirmed pattern matches default suggestion (confidence: {confidence:P0})");
        }
        
        Console.WriteLine();

        while (true)
        {
            var confirmResult = _promptService.SelectPrompt<string>(new SelectPromptOptions
            {
                Question = "What would you like to do?",
                Choices = new List<PromptChoice>
                {
                    new("accept", "Accept suggested episode", "accept"),
                    new("accept_all", "Accept all suggestions for this disc", "accept_all"),
                    new("choose", "Choose different episode", "choose"),
                    new("skip", "Skip this track (move to _trash folder)", "skip")
                }
            });

            if (!confirmResult.Success || confirmResult.Cancelled)
            {
                _logger.LogInformation($"User cancelled episode confirmation for track {trackName}, using suggested episode {finalSuggestion}");
                
                // Record the selection for pattern learning (only for UserConfirmed strategy)
                if (!string.IsNullOrEmpty(discName) && !string.IsNullOrEmpty(trackId) && trackPosition >= 0)
                {
                    _patternLearningService.RecordSelection(seriesTitle, season, discName, trackPosition, 
                                                           trackId, trackName, finalSuggestion, finalSuggestion, true);
                }
                
                return finalSuggestion;
            }

            if (confirmResult.Value == "accept" || confirmResult.Value == "accept_all")
            {
                // Enable auto-accept for the rest of the disc if requested
                if (confirmResult.Value == "accept_all")
                {
                    _acceptAllSuggestionsForDisc = true;
                    _logger.LogInformation("User enabled auto-accept for all episode suggestions for this disc");
                }
                
                var acceptedEpisode = finalSuggestion;
                _logger.LogInformation($"User accepted suggested episode {acceptedEpisode} for track {trackName}");
                
                // Record the selection for pattern learning (only for UserConfirmed strategy)
                if (!string.IsNullOrEmpty(discName) && !string.IsNullOrEmpty(trackId) && trackPosition >= 0)
                {
                    _patternLearningService.RecordSelection(seriesTitle, season, discName, trackPosition, 
                                                           trackId, trackName, finalSuggestion, acceptedEpisode, true);
                }
                
                return acceptedEpisode;
            }
            else if (confirmResult.Value == "skip")
            {
                _logger.LogInformation($"User chose to skip track {trackName} - will be moved to _trash folder");
                
                // Record the skip selection for pattern learning (only for UserConfirmed strategy)
                if (!string.IsNullOrEmpty(discName) && !string.IsNullOrEmpty(trackId) && trackPosition >= 0)
                {
                    _patternLearningService.RecordSelection(seriesTitle, season, discName, trackPosition, 
                                                           trackId, trackName, finalSuggestion, -1, false);
                }
                
                return null; // Indicates the track should be skipped
            }
            else
            {
                var selectedEpisode = await SelectFromAvailableEpisodesAsync(seriesTitle, season, availableEpisodes, enhancedOmdbService, trackName);
                
                // Record the selection for pattern learning (only for UserConfirmed strategy)
                if (!string.IsNullOrEmpty(discName) && !string.IsNullOrEmpty(trackId) && trackPosition >= 0)
                {
                    _patternLearningService.RecordSelection(seriesTitle, season, discName, trackPosition, 
                                                           trackId, trackName, finalSuggestion, selectedEpisode, false);
                }
                
                return selectedEpisode;
            }
        }
    }

    private async Task<int> SelectFromAvailableEpisodesAsync(string seriesTitle, int season, List<int> availableEpisodes, IEnhancedOmdbService enhancedOmdbService, string trackName)
    {
        _promptService.DisplayHeader("Available episodes that haven't been picked");

        // Display all available episodes with their titles
        var episodeInfos = new Dictionary<int, string>();
        foreach (var episodeNum in availableEpisodes.OrderBy(x => x))
        {
            try
            {
                var episodeInfo = await enhancedOmdbService.GetEpisodeInfoAsync(seriesTitle, season, episodeNum);
                var title = episodeInfo?.Title ?? "(No info available)";
                episodeInfos[episodeNum] = title;
                Console.WriteLine($"  {episodeNum}. Episode {episodeNum}: {title}");
            }
            catch
            {
                episodeInfos[episodeNum] = "(Error retrieving info)";
                Console.WriteLine($"  {episodeNum}. Episode {episodeNum}: (Error retrieving info)");
            }
        }
        
        Console.WriteLine();

        var choices = new List<PromptChoice>();
        foreach (var episodeNum in availableEpisodes.OrderBy(x => x))
        {
            var title = episodeInfos.GetValueOrDefault(episodeNum, "(Unknown)");
            choices.Add(new(episodeNum.ToString(), $"Episode {episodeNum}: {title}", episodeNum));
        }

        var selectionResult = _promptService.SelectPrompt<int>(new SelectPromptOptions
        {
            Question = "Select episode:",
            Choices = choices
        });

        if (!selectionResult.Success || selectionResult.Cancelled)
        {
            var firstEpisode = availableEpisodes.OrderBy(x => x).First();
            _logger.LogInformation($"User cancelled episode selection for track {trackName}, using first available episode {firstEpisode}");
            return firstEpisode;
        }

        var selectedEpisode = selectionResult.Value;
        var episodeTitle = episodeInfos.GetValueOrDefault(selectedEpisode, "Unknown");
        Console.WriteLine($"Selected: Episode {selectedEpisode} - {episodeTitle}");
        _logger.LogInformation($"User selected episode {selectedEpisode} ({episodeTitle}) for track {trackName}");
        return selectedEpisode;
    }

    /// <summary>
    /// Completes pattern learning for a disc by analyzing all user selections
    /// Only processes patterns if TrackSortingStrategy is UserConfirmed for this series
    /// </summary>
    /// <param name="seriesTitle">The series title</param>
    /// <param name="season">The season number</param>
    /// <param name="discName">The disc name</param>
    /// <param name="userSelections">List of all user selections for the disc</param>
    public async Task CompletePatternLearningAsync(string seriesTitle, int season, string discName, List<TrackSelectionPattern> userSelections)
    {
        if (userSelections.Any())
        {
            _logger.LogInformation($"Analyzing UserConfirmed pattern learning for {seriesTitle} S{season:D2} disc '{discName}' with {userSelections.Count} selections");
            await _patternLearningService.AnalyzeAndUpdatePatternsAsync(seriesTitle, season, discName, userSelections);
        }
    }

    /// <summary>
    /// Gets a preview of what pattern learning would suggest for future discs
    /// Only provides previews if TrackSortingStrategy is UserConfirmed for this series
    /// </summary>
    /// <param name="seriesTitle">The series title</param>
    /// <param name="season">The season number</param>
    /// <param name="discName">The disc name</param>
    /// <param name="trackCount">Number of tracks on the disc</param>
    /// <returns>List of track position to episode suggestions</returns>
    public List<(int trackPosition, int suggestedEpisode, double confidence)> GetPatternPreviews(string seriesTitle, int season, string discName, int trackCount)
    {
        var previews = new List<(int trackPosition, int suggestedEpisode, double confidence)>();
        
        for (int i = 0; i < trackCount; i++)
        {
            var (episode, confidence) = _patternLearningService.GetSuggestedEpisode(seriesTitle, season, discName, i, i + 1);
            previews.Add((i, episode, confidence));
        }
        
        return previews;
    }

    /// <summary>
    /// Resets the disc-wide auto-accept flag for processing a new disc
    /// This should be called at the start of each new disc processing
    /// </summary>
    public void ResetDiscAutoAccept()
    {
        _acceptAllSuggestionsForDisc = false;
        _logger.LogDebug("Reset disc-wide auto-accept flag for new disc processing");
    }

    private (double minSize, double maxSize) PromptForSizeRange()
    {
        double minSize, maxSize;
        
        // Get minimum size
        var minSizeResult = _promptService.TextPrompt(new TextPromptOptions
        {
            Question = "Enter minimum episode size in GB (e.g., 0.5 for 500MB, 1.2 for 1.2GB):",
            Required = true,
            PromptText = "Min Size (GB)",
            ValidationPattern = @"^\d+(\.\d+)?$",
            ValidationMessage = "Please enter a valid number"
        });

        if (!minSizeResult.Success || !double.TryParse(minSizeResult.Value, out minSize) || minSize <= 0 || minSize > 50)
        {
            _logger.LogWarning("Invalid minimum size entered, using default 0.5 GB");
            minSize = 0.5;
        }
        
        Console.WriteLine($"Minimum episode size set to {minSize} GB");
        
        // Get maximum size
        var maxSizeResult = _promptService.TextPrompt(new TextPromptOptions
        {
            Question = $"Enter maximum episode size in GB (must be >= {minSize} GB):",
            Required = true,
            PromptText = "Max Size (GB)",
            ValidationPattern = @"^\d+(\.\d+)?$",
            ValidationMessage = "Please enter a valid number"
        });

        if (!maxSizeResult.Success || !double.TryParse(maxSizeResult.Value, out maxSize) || maxSize < minSize || maxSize > 100)
        {
            _logger.LogWarning($"Invalid maximum size entered, using {Math.Max(minSize * 2, 10)} GB");
            maxSize = Math.Max(minSize * 2, 10);
        }

        Console.WriteLine($"Maximum episode size set to {maxSize} GB");
        _logger.LogInformation($"User set episode size range: {minSize} - {maxSize} GB");
        return (minSize, maxSize);
    }

    private (int minChapters, int maxChapters) PromptForChapterRange()
    {
        int minChapters, maxChapters;

        // Get minimum chapters
        var minChaptersResult = _promptService.TextPrompt(new TextPromptOptions
        {
            Question = "Enter minimum episode chapter count (e.g., 1, 5, 10):",
            Required = true,
            PromptText = "Min Chapters",
            ValidationPattern = @"^\d+$",
            ValidationMessage = "Please enter a valid positive integer"
        });

        if (!minChaptersResult.Success || !int.TryParse(minChaptersResult.Value, out minChapters) || minChapters <= 0 || minChapters > 999)
        {
            _logger.LogWarning("Invalid minimum chapter count entered, using default 1");
            minChapters = 1;
        }

        Console.WriteLine($"Minimum episode chapters set to {minChapters}");

        // Get maximum chapters
        var maxChaptersResult = _promptService.TextPrompt(new TextPromptOptions
        {
            Question = $"Enter maximum episode chapter count (must be >= {minChapters}):",
            Required = true,
            PromptText = "Max Chapters",
            ValidationPattern = @"^\d+$",
            ValidationMessage = "Please enter a valid positive integer"
        });

        if (!maxChaptersResult.Success || !int.TryParse(maxChaptersResult.Value, out maxChapters) || maxChapters < minChapters || maxChapters > 999)
        {
            _logger.LogWarning($"Invalid maximum chapter count entered, using {Math.Max(minChapters * 2, 50)}");
            maxChapters = Math.Max(minChapters * 2, 50);
        }

        Console.WriteLine($"Maximum episode chapters set to {maxChapters}");
        _logger.LogInformation($"User set episode chapter range: {minChapters} - {maxChapters}");
        return (minChapters, maxChapters);
    }
}