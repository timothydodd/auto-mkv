using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMk.Models;

namespace AutoMk.Interfaces;

/// <summary>
/// Service for handling TV series configuration and profile management UI interactions
/// </summary>
public interface ISeriesConfigurationService
{
    /// <summary>
    /// Prompts user for starting season and episode information for a new series disc
    /// </summary>
    /// <param name="seriesTitle">The title of the TV series</param>
    /// <param name="discName">The name of the disc being processed</param>
    /// <returns>Tuple containing the season and episode numbers</returns>
    (int season, int episode) PromptForStartingSeasonAndEpisode(string seriesTitle, string discName);

    /// <summary>
    /// Prompts user for episode size filtering range for a series
    /// </summary>
    /// <param name="seriesTitle">The title of the TV series</param>
    /// <returns>Tuple containing min and max episode sizes in GB, or null for defaults</returns>
    (double? minSize, double? maxSize) PromptForEpisodeSizeRange(string seriesTitle);

    /// <summary>
    /// Prompts user for track sorting strategy for a series
    /// </summary>
    /// <param name="seriesTitle">The title of the TV series</param>
    /// <returns>The selected track sorting strategy</returns>
    TrackSortingStrategy PromptForTrackSortingStrategy(string seriesTitle);

    /// <summary>
    /// Prompts user for double episode handling strategy for a specific track
    /// </summary>
    /// <param name="seriesTitle">The title of the TV series</param>
    /// <param name="trackName">The name of the track</param>
    /// <param name="trackLengthSeconds">The length of the track in seconds</param>
    /// <param name="minEpisodeLengthSeconds">The minimum episode length in seconds</param>
    /// <returns>Tuple containing whether to treat as double and optional preference to save</returns>
    (bool treatAsDouble, DoubleEpisodeHandling? savePreference) PromptForDoubleEpisodeHandling(
        string seriesTitle, 
        string trackName, 
        double trackLengthSeconds, 
        double minEpisodeLengthSeconds);

    /// <summary>
    /// Prompts user to configure all settings for a new TV series
    /// </summary>
    /// <param name="seriesTitle">The title of the TV series</param>
    /// <param name="discName">The name of the disc being processed</param>
    /// <returns>A complete series profile with all user-configured settings</returns>
    SeriesProfile PromptForCompleteSeriesProfile(string seriesTitle, string discName);

    /// <summary>
    /// Prompts user to modify existing series profile settings
    /// </summary>
    /// <param name="existingProfile">The current series profile</param>
    /// <param name="discName">The name of the disc being processed</param>
    /// <returns>A modified series profile with updated settings</returns>
    SeriesProfile PromptForModifySeriesProfile(SeriesProfile existingProfile, string discName);

    /// <summary>
    /// Prompts user to confirm or select the correct episode number for a track
    /// </summary>
    /// <param name="seriesTitle">The title of the TV series</param>
    /// <param name="season">The season number</param>
    /// <param name="suggestedEpisode">The suggested episode number</param>
    /// <param name="episodeTitle">The title of the episode</param>
    /// <param name="trackName">The name of the track</param>
    /// <param name="availableEpisodes">List of available episode numbers</param>
    /// <param name="enhancedOmdbService">Service for fetching episode information</param>
    /// <param name="allTracks">Optional list of all tracks for context</param>
    /// <returns>The confirmed episode number</returns>
    Task<int> ConfirmOrSelectEpisodeAsync(
        string seriesTitle, 
        int season, 
        int suggestedEpisode, 
        string episodeTitle, 
        string trackName, 
        List<int> availableEpisodes, 
        IEnhancedOmdbService enhancedOmdbService, 
        List<AkTitle>? allTracks = null);

    /// <summary>
    /// Enhanced version with pattern learning support for UserConfirmed sorting strategy
    /// </summary>
    /// <param name="seriesTitle">The title of the TV series</param>
    /// <param name="season">The season number</param>
    /// <param name="suggestedEpisode">The suggested episode number</param>
    /// <param name="episodeTitle">The title of the episode</param>
    /// <param name="trackName">The name of the track</param>
    /// <param name="availableEpisodes">List of available episode numbers</param>
    /// <param name="enhancedOmdbService">Service for fetching episode information</param>
    /// <param name="allTracks">Optional list of all tracks for context</param>
    /// <param name="discName">The disc name for pattern matching</param>
    /// <param name="trackId">The track ID</param>
    /// <param name="trackPosition">The track position (0-based)</param>
    /// <returns>The confirmed episode number</returns>
    Task<int> ConfirmOrSelectEpisodeWithPatternLearningAsync(
        string seriesTitle, 
        int season, 
        int suggestedEpisode, 
        string episodeTitle, 
        string trackName, 
        List<int> availableEpisodes, 
        IEnhancedOmdbService enhancedOmdbService, 
        List<AkTitle>? allTracks,
        string discName,
        string trackId,
        int trackPosition);

    /// <summary>
    /// Completes pattern learning for a disc by analyzing all user selections
    /// Only processes patterns if TrackSortingStrategy is UserConfirmed for this series
    /// </summary>
    /// <param name="seriesTitle">The series title</param>
    /// <param name="season">The season number</param>
    /// <param name="discName">The disc name</param>
    /// <param name="userSelections">List of all user selections for the disc</param>
    Task CompletePatternLearningAsync(string seriesTitle, int season, string discName, List<TrackSelectionPattern> userSelections);
}