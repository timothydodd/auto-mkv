using System;

namespace AutoMk.Models;

/// <summary>
/// Optimized model for TV series processing operations.
/// Contains only the essential properties needed for series identification and processing.
/// </summary>
public class SeriesInfo
{
    /// <summary>
    /// The series title
    /// </summary>
    public string? Title { get; set; }
    
    /// <summary>
    /// The release year
    /// </summary>
    public string? Year { get; set; }
    
    /// <summary>
    /// The media type (should always be "series")
    /// </summary>
    public string? Type { get; set; }
    
    /// <summary>
    /// The IMDb ID
    /// </summary>
    public string? ImdbID { get; set; }
    
    /// <summary>
    /// Total number of seasons
    /// </summary>
    public string? TotalSeasons { get; set; }
    
    
    /// <summary>
    /// Creates SeriesInfo from an OmdbSeriesResponse
    /// </summary>
    public static SeriesInfo FromSeriesResponse(OmdbSeriesResponse seriesResponse)
    {
        return new SeriesInfo
        {
            Title = seriesResponse.Title,
            Year = seriesResponse.Year,
            Type = seriesResponse.Type,
            ImdbID = seriesResponse.ImdbID,
            TotalSeasons = seriesResponse.TotalSeasons
        };
    }
    
    /// <summary>
    /// Creates SeriesInfo from a OptimizedSearchResult
    /// </summary>
    public static SeriesInfo FromOptimizedSearchResult(OptimizedSearchResult searchResult, string? totalSeasons = null)
    {
        return new SeriesInfo
        {
            Title = searchResult.Title,
            Year = searchResult.Year,
            Type = searchResult.Type,
            ImdbID = searchResult.ImdbID,
            TotalSeasons = totalSeasons
        };
    }
    
    /// <summary>
    /// Creates SeriesInfo from MediaIdentity
    /// </summary>
    public static SeriesInfo FromMediaIdentity(MediaIdentity identity, string? totalSeasons = null)
    {
        return new SeriesInfo
        {
            Title = identity.Title,
            Year = identity.Year,
            Type = identity.Type,
            ImdbID = identity.ImdbID,
            TotalSeasons = totalSeasons
        };
    }
    
    /// <summary>
    /// Creates SeriesInfo from MediaDetails
    /// </summary>
    public static SeriesInfo FromMediaDetails(MediaDetails details)
    {
        return new SeriesInfo
        {
            Title = details.Identity.Title,
            Year = details.Identity.Year,
            Type = details.Identity.Type,
            ImdbID = details.Identity.ImdbID,
            TotalSeasons = details.TotalSeasons
        };
    }
    
    /// <summary>
    /// Converts this SeriesInfo to a MediaIdentity
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
    /// Converts this SeriesInfo to MediaDetails
    /// </summary>
    public MediaDetails ToMediaDetails(string? plot = null)
    {
        return new MediaDetails
        {
            Identity = ToMediaIdentity(),
            Plot = plot,
            TotalSeasons = TotalSeasons
        };
    }
    
    /// <summary>
    /// Validates that this series info has the minimum required data
    /// </summary>
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(Title) &&
               !string.IsNullOrWhiteSpace(ImdbID) &&
               Type?.Equals("series", StringComparison.OrdinalIgnoreCase) == true;
    }
    
    /// <summary>
    /// Gets the total seasons as an integer, or null if not available/parseable
    /// </summary>
    public int? GetTotalSeasonsAsInt()
    {
        if (int.TryParse(TotalSeasons, out int seasons) && seasons > 0)
        {
            return seasons;
        }
        return null;
    }
}