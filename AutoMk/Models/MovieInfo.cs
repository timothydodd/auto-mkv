using System;

namespace AutoMk.Models;

/// <summary>
/// Optimized model for movie processing operations.
/// Contains only the essential properties needed for movie identification and processing.
/// </summary>
public class MovieInfo
{
    /// <summary>
    /// The movie title
    /// </summary>
    public string? Title { get; set; }
    
    /// <summary>
    /// The release year
    /// </summary>
    public string? Year { get; set; }
    
    /// <summary>
    /// The media type (should always be "movie")
    /// </summary>
    public string? Type { get; set; }
    
    /// <summary>
    /// The IMDb ID
    /// </summary>
    public string? ImdbID { get; set; }
    
    
    /// <summary>
    /// Creates MovieInfo from an OmdbMovieResponse
    /// </summary>
    public static MovieInfo FromMovieResponse(OmdbMovieResponse movieResponse)
    {
        return new MovieInfo
        {
            Title = movieResponse.Title,
            Year = movieResponse.Year,
            Type = movieResponse.Type,
            ImdbID = movieResponse.ImdbID
        };
    }
    
    /// <summary>
    /// Creates MovieInfo from a OptimizedSearchResult
    /// </summary>
    public static MovieInfo FromOptimizedSearchResult(OptimizedSearchResult searchResult)
    {
        return new MovieInfo
        {
            Title = searchResult.Title,
            Year = searchResult.Year,
            Type = searchResult.Type,
            ImdbID = searchResult.ImdbID
        };
    }
    
    /// <summary>
    /// Creates MovieInfo from MediaIdentity
    /// </summary>
    public static MovieInfo FromMediaIdentity(MediaIdentity identity)
    {
        return new MovieInfo
        {
            Title = identity.Title,
            Year = identity.Year,
            Type = identity.Type,
            ImdbID = identity.ImdbID
        };
    }
    
    /// <summary>
    /// Converts this MovieInfo to a MediaIdentity
    /// </summary>
    public MediaIdentity ToMediaIdentity()
    {
        return new MediaIdentity
        {
            Title = Title,
            Year = Year,
            Type = Type,
            ImdbID = ImdbID,
            Response = "True"
        };
    }
    
    /// <summary>
    /// Validates that this movie info has the minimum required data
    /// </summary>
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(Title) &&
               !string.IsNullOrWhiteSpace(ImdbID) &&
               Type?.Equals("movie", StringComparison.OrdinalIgnoreCase) == true;
    }
}