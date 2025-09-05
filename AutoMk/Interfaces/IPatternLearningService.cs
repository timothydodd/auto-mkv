using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMk.Models;

namespace AutoMk.Interfaces;

/// <summary>
/// Service for learning and applying user selection patterns for episode-to-track mapping
/// Only used when TrackSortingStrategy is set to UserConfirmed for the same series
/// </summary>
public interface IPatternLearningService
{
    /// <summary>
    /// Records a user's episode selection for pattern learning
    /// </summary>
    /// <param name="seriesTitle">The series title</param>
    /// <param name="season">The season number</param>
    /// <param name="discName">The disc name</param>
    /// <param name="trackPosition">Position of track in the list (0-based)</param>
    /// <param name="trackId">The track ID</param>
    /// <param name="trackName">The track name</param>
    /// <param name="suggestedEpisode">Episode number that was suggested</param>
    /// <param name="selectedEpisode">Episode number that user actually selected</param>
    /// <param name="wasAccepted">Whether user accepted the suggestion</param>
    void RecordSelection(string seriesTitle, int season, string discName, int trackPosition, 
                        string trackId, string trackName, int suggestedEpisode, int selectedEpisode, bool wasAccepted);

    /// <summary>
    /// Gets suggested episode number based on learned patterns for UserConfirmed strategy
    /// Only returns suggestions when TrackSortingStrategy is UserConfirmed for this series
    /// </summary>
    /// <param name="seriesTitle">The series title</param>
    /// <param name="season">The season number</param>
    /// <param name="discName">The disc name (not used for pattern matching, only for logging)</param>
    /// <param name="trackPosition">Position of track in the list (0-based)</param>
    /// <param name="fallbackEpisode">Fallback episode number if no pattern found</param>
    /// <returns>Suggested episode number and confidence score</returns>
    (int episodeNumber, double confidence) GetSuggestedEpisode(string seriesTitle, int season, 
                                                              string discName, int trackPosition, int fallbackEpisode);

    /// <summary>
    /// Analyzes all user selections for a disc and creates/updates learned patterns
    /// </summary>
    /// <param name="seriesTitle">The series title</param>
    /// <param name="season">The season number</param>
    /// <param name="discName">The disc name</param>
    /// <param name="userSelections">List of user selections for the disc</param>
    Task AnalyzeAndUpdatePatternsAsync(string seriesTitle, int season, string discName, 
                                      List<TrackSelectionPattern> userSelections);

    /// <summary>
    /// Gets the confidence score for pattern suggestions for a specific disc type
    /// </summary>
    /// <param name="seriesTitle">The series title</param>
    /// <param name="season">The season number</param>
    /// <param name="discName">The disc name</param>
    /// <returns>Confidence score (0.0 to 1.0) or 0.0 if no patterns exist</returns>
    double GetPatternConfidence(string seriesTitle, int season, string discName);

    /// <summary>
    /// Checks if there are any learned patterns for a specific series and disc type
    /// </summary>
    /// <param name="seriesTitle">The series title</param>
    /// <param name="season">The season number</param>
    /// <param name="discName">The disc name</param>
    /// <returns>True if patterns exist, false otherwise</returns>
    bool HasLearnedPatterns(string seriesTitle, int season, string discName);
}