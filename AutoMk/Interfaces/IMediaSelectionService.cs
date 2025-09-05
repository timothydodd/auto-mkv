using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMk.Models;

namespace AutoMk.Interfaces;

/// <summary>
/// Service for handling media search and selection UI interactions
/// </summary>
public interface IMediaSelectionService
{
    /// <summary>
    /// Provides interactive media search functionality when automatic identification fails
    /// </summary>
    /// <param name="originalTitle">The original title that failed to identify</param>
    /// <param name="discName">The name of the disc being processed</param>
    /// <returns>The selected search result or null if cancelled/skipped</returns>
    Task<OptimizedSearchResult?> InteractiveMediaSearchAsync(string originalTitle, string discName);

    /// <summary>
    /// Confirms whether the automatically identified media is correct
    /// </summary>
    /// <param name="mediaData">The media data to confirm</param>
    /// <param name="discName">The name of the disc being processed</param>
    /// <returns>True if confirmed, false if rejected</returns>
    bool ConfirmMediaIdentification(ConfirmationInfo mediaData, string discName);

    /// <summary>
    /// Allows user to search for media by title when automatic identification fails
    /// </summary>
    /// <param name="searchQuery">The search query to use</param>
    /// <param name="mediaType">The type of media to search for</param>
    /// <returns>The search results or null if cancelled</returns>
    Task<OptimizedSearchResult[]?> SearchMediaByTitleAsync(string searchQuery, MediaTypePrediction mediaType);

    /// <summary>
    /// Prompts user to select from a list of search results
    /// </summary>
    /// <param name="searchResults">List of search results to choose from</param>
    /// <param name="searchQuery">The original search query</param>
    /// <returns>The selected search result or null if cancelled</returns>
    OptimizedSearchResult? SelectFromSearchResults(List<OptimizedSearchResult> searchResults, string searchQuery);

    /// <summary>
    /// Resolves conflicts when both movie and series matches are found
    /// </summary>
    /// <param name="discName">The name of the disc being processed</param>
    /// <param name="movieResult">The movie match found</param>
    /// <param name="seriesResult">The series match found</param>
    /// <returns>The selected media identity or null if cancelled</returns>
    Task<MediaIdentity?> SelectBetweenMovieAndSeriesAsync(string discName, ConfirmationInfo movieResult, ConfirmationInfo seriesResult);
}