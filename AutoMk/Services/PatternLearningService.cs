using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AutoMk.Interfaces;
using AutoMk.Models;
using AutoMk.Utilities;
using Microsoft.Extensions.Logging;

namespace AutoMk.Services;

/// <summary>
/// Service for learning and applying user selection patterns for episode-to-track mapping
/// </summary>
public class PatternLearningService : IPatternLearningService
{
    private readonly IMediaStateManager _stateManager;
    private readonly ILogger<PatternLearningService> _logger;
    private readonly IConsoleOutputService? _consoleOutput;
    private const double MINIMUM_CONFIDENCE_THRESHOLD = 0.7;
    private const int MINIMUM_PATTERN_SAMPLES = 2; // Minimum number of discs needed to establish a pattern

    public PatternLearningService(IMediaStateManager stateManager, ILogger<PatternLearningService> logger, IConsoleOutputService? consoleOutput = null)
    {
        _stateManager = ValidationHelper.ValidateNotNull(stateManager);
        _logger = ValidationHelper.ValidateNotNull(logger);
        _consoleOutput = consoleOutput;
    }

    /// <summary>
    /// Records a user's episode selection for pattern learning
    /// </summary>
    public void RecordSelection(string seriesTitle, int season, string discName, int trackPosition, 
                               string trackId, string trackName, int suggestedEpisode, int selectedEpisode, bool wasAccepted)
    {
        var selection = new TrackSelectionPattern
        {
            TrackId = trackId,
            TrackName = trackName,
            TrackOrderPosition = trackPosition,
            SuggestedEpisode = suggestedEpisode,
            SelectedEpisode = selectedEpisode,
            WasAccepted = wasAccepted,
            SelectionDate = DateTime.Now,
            SelectionReason = wasAccepted ? "accepted" : "manual_choice"
        };

        _logger.LogInformation($"Recording selection pattern: {seriesTitle} S{season:D2} Track {trackPosition} -> Episode {selectedEpisode} (Suggested: {suggestedEpisode}, Accepted: {wasAccepted})");
        
        // The selection will be stored in the DiscInfo when the disc processing is completed
        // This method primarily serves to log and prepare the data
    }

    /// <summary>
    /// Gets suggested episode number based on learned patterns for UserConfirmed sorting strategy
    /// </summary>
    public (int episodeNumber, double confidence) GetSuggestedEpisode(string seriesTitle, int season, 
                                                                     string discName, int trackPosition, int fallbackEpisode)
    {
        var seriesState = _stateManager.GetExistingSeriesStateAsync(seriesTitle).Result;
        
        // Only use patterns if TrackSortingStrategy is UserConfirmed
        if (seriesState?.TrackSortingStrategy != TrackSortingStrategy.UserConfirmed ||
            seriesState?.LearnedPatterns == null || !seriesState.LearnedPatterns.Any())
        {
            return (fallbackEpisode, 0.0);
        }

        // Only look for patterns within the SAME series (exact title match) and season
        var matchingPatterns = seriesState.LearnedPatterns
            .Where(p => p.Season == season && 
                       p.TrackMappings.Any(tm => tm.TrackPosition == trackPosition))
            .OrderByDescending(p => p.ConfidenceScore)
            .ToList();

        if (!matchingPatterns.Any())
        {
            _logger.LogDebug($"No learned patterns found for {seriesTitle} S{season:D2} track position {trackPosition}");
            return (fallbackEpisode, 0.0);
        }

        var bestPattern = matchingPatterns.First();
        var trackMapping = bestPattern.TrackMappings.First(tm => tm.TrackPosition == trackPosition);
        
        // Calculate the starting episode for the current disc (assuming sequential episodes starting from fallbackEpisode - trackPosition)
        // We need to determine what episode this disc starts with
        var currentDiscStartEpisode = CalculateDiscStartingEpisode(fallbackEpisode, trackPosition, bestPattern.TrackMappings.Count);
        
        // Convert stored absolute episode number to relative position within the original disc
        var originalDiscEpisodes = bestPattern.TrackMappings.Select(tm => tm.EpisodeNumber).OrderBy(e => e).ToList();
        var minOriginalEpisode = originalDiscEpisodes.Min();
        var relativePosition = trackMapping.EpisodeNumber - minOriginalEpisode; // 0-based relative position
        
        // Apply the relative position to the current disc's starting episode
        var suggestedEpisode = currentDiscStartEpisode + relativePosition;
        
        _logger.LogInformation($"Found learned pattern for {seriesTitle} S{season:D2} track {trackPosition}: Relative position {relativePosition} → Episode {suggestedEpisode} (confidence: {trackMapping.ConfidenceScore:F2})");
        
        // Show console feedback when using learned patterns
        if (_consoleOutput != null && trackMapping.ConfidenceScore >= MINIMUM_CONFIDENCE_THRESHOLD)
        {
            _consoleOutput.ShowInfo($"Using learned pattern: Track {trackPosition} → Episode {suggestedEpisode} (confidence: {trackMapping.ConfidenceScore:P0})");
        }
        
        return (suggestedEpisode, trackMapping.ConfidenceScore);
    }

    /// <summary>
    /// Calculates the starting episode number for the current disc based on the fallback episode and track position
    /// </summary>
    private int CalculateDiscStartingEpisode(int fallbackEpisode, int trackPosition, int totalTracksInPattern)
    {
        // The fallbackEpisode represents what episode this track would be if episodes were sequential
        // So the starting episode for this disc would be: fallbackEpisode - trackPosition
        return fallbackEpisode - trackPosition;
    }

    /// <summary>
    /// Analyzes all user selections for a disc and creates/updates learned patterns
    /// </summary>
    public async Task AnalyzeAndUpdatePatternsAsync(string seriesTitle, int season, string discName, 
                                                   List<TrackSelectionPattern> userSelections)
    {
        if (!userSelections.Any())
        {
            return;
        }

        var seriesState = await _stateManager.GetOrCreateSeriesStateAsync(seriesTitle);
        if (seriesState == null)
        {
            _logger.LogWarning($"No series state found for {seriesTitle}");
            return;
        }

        // For UserConfirmed strategy, we only care about the season, not disc patterns
        // since it's about learning user's episode confirmation choices for the same series
        var existingPattern = seriesState.LearnedPatterns
            .FirstOrDefault(p => p.Season == season);

        if (existingPattern != null)
        {
            // Update existing pattern
            var oldConfidence = existingPattern.ConfidenceScore;
            UpdateExistingPattern(existingPattern, userSelections);
            var newConfidence = existingPattern.ConfidenceScore;
            
            _logger.LogInformation($"Updated existing UserConfirmed pattern for {seriesTitle} S{season:D2}");
            
            if (_consoleOutput != null)
            {
                _consoleOutput.ShowInfo($"Updated learning pattern for {seriesTitle} S{season:D2} (confidence: {oldConfidence:P0} → {newConfidence:P0})");
            }
        }
        else
        {
            // Create new pattern
            var newPattern = CreateNewPattern(season, userSelections);
            seriesState.LearnedPatterns.Add(newPattern);
            _logger.LogInformation($"Created new UserConfirmed pattern for {seriesTitle} S{season:D2}");
            
            if (_consoleOutput != null)
            {
                _consoleOutput.ShowInfo($"Created new learning pattern for {seriesTitle} S{season:D2} (confidence: {newPattern.ConfidenceScore:P0})");
            }
        }

        await _stateManager.SaveSeriesStateAsync(seriesState);
    }

    /// <summary>
    /// Gets the confidence score for pattern suggestions for UserConfirmed sorting strategy
    /// </summary>
    public double GetPatternConfidence(string seriesTitle, int season, string discName)
    {
        var seriesState = _stateManager.GetExistingSeriesStateAsync(seriesTitle).Result;
        
        // Only provide confidence if TrackSortingStrategy is UserConfirmed
        if (seriesState?.TrackSortingStrategy != TrackSortingStrategy.UserConfirmed ||
            seriesState?.LearnedPatterns == null || !seriesState.LearnedPatterns.Any())
        {
            return 0.0;
        }

        // Get patterns for this specific series and season only
        var matchingPattern = seriesState.LearnedPatterns
            .Where(p => p.Season == season)
            .OrderByDescending(p => p.ConfidenceScore)
            .FirstOrDefault();

        return matchingPattern?.ConfidenceScore ?? 0.0;
    }

    /// <summary>
    /// Checks if there are any learned patterns for UserConfirmed sorting strategy
    /// </summary>
    public bool HasLearnedPatterns(string seriesTitle, int season, string discName)
    {
        var seriesState = _stateManager.GetExistingSeriesStateAsync(seriesTitle).Result;
        
        // Only use patterns if TrackSortingStrategy is UserConfirmed
        if (seriesState?.TrackSortingStrategy != TrackSortingStrategy.UserConfirmed)
        {
            return false;
        }
        
        var confidence = GetPatternConfidence(seriesTitle, season, discName);
        return confidence >= MINIMUM_CONFIDENCE_THRESHOLD;
    }

    private string NormalizeDiscName(string discName)
    {
        // Convert specific disc names to patterns
        // e.g., "Frasier_S8_D1_BD" -> "Frasier_S*_D*_BD"
        var pattern = discName;
        
        // Replace season numbers with wildcards
        pattern = Regex.Replace(pattern, @"S\d+", "S*", RegexOptions.IgnoreCase);
        
        // Replace disc numbers with wildcards
        pattern = Regex.Replace(pattern, @"D\d+", "D*", RegexOptions.IgnoreCase);
        
        // Replace any remaining numbers with wildcards (for variations)
        pattern = Regex.Replace(pattern, @"\d+", "*");
        
        return pattern;
    }

    private bool PatternMatches(string learnedPattern, string currentDiscPattern)
    {
        // Simple pattern matching - could be enhanced with more sophisticated matching
        return learnedPattern.Equals(currentDiscPattern, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateExistingPattern(EpisodeTrackPattern existingPattern, List<TrackSelectionPattern> userSelections)
    {
        existingPattern.UsageCount++;
        existingPattern.LastUsed = DateTime.Now;

        // Update track mappings based on user selections
        foreach (var selection in userSelections)
        {
            var existingMapping = existingPattern.TrackMappings
                .FirstOrDefault(tm => tm.TrackPosition == selection.TrackOrderPosition);

            if (existingMapping != null)
            {
                // Update confidence based on whether user accepted or changed the suggestion
                if (selection.WasAccepted)
                {
                    // Increase confidence if user accepted suggestion
                    existingMapping.ConfidenceScore = Math.Min(1.0, existingMapping.ConfidenceScore + 0.1);
                }
                else
                {
                    // If user chose different episode, update the mapping and adjust confidence
                    if (existingMapping.EpisodeNumber != selection.SelectedEpisode)
                    {
                        existingMapping.EpisodeNumber = selection.SelectedEpisode;
                        existingMapping.ConfidenceScore = 0.5; // Reset confidence when changing mapping
                    }
                }
            }
            else
            {
                // Add new track mapping
                existingPattern.TrackMappings.Add(new TrackToEpisodeMapping
                {
                    TrackPosition = selection.TrackOrderPosition,
                    EpisodeNumber = selection.SelectedEpisode,
                    ConfidenceScore = selection.WasAccepted ? 0.8 : 0.6
                });
            }
        }

        // Update overall pattern confidence
        existingPattern.ConfidenceScore = CalculateOverallConfidence(existingPattern.TrackMappings);
    }

    private EpisodeTrackPattern CreateNewPattern(int season, List<TrackSelectionPattern> userSelections)
    {
        var trackMappings = userSelections.Select(selection => new TrackToEpisodeMapping
        {
            TrackPosition = selection.TrackOrderPosition,
            EpisodeNumber = selection.SelectedEpisode,
            ConfidenceScore = selection.WasAccepted ? 0.8 : 0.6
        }).ToList();

        return new EpisodeTrackPattern
        {
            DiscNamePattern = "UserConfirmed", // Not disc-specific, just indicates this is for UserConfirmed strategy
            Season = season,
            TrackMappings = trackMappings,
            UsageCount = 1,
            ConfidenceScore = CalculateOverallConfidence(trackMappings),
            LastUsed = DateTime.Now,
            CreatedDate = DateTime.Now
        };
    }

    private double CalculateOverallConfidence(List<TrackToEpisodeMapping> trackMappings)
    {
        if (!trackMappings.Any())
        {
            return 0.0;
        }

        // Calculate average confidence of all track mappings
        return trackMappings.Average(tm => tm.ConfidenceScore);
    }
}