using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace AutoMk.Models;

/// <summary>
/// Base class for OMDB API responses containing common properties
/// NOTE: Optimized to remove unused properties for better performance
/// </summary>
public abstract class OmdbBaseResponse
{
    // Core identification properties
    public string? Title { get; set; }
    public string? Year { get; set; }
    public string? ImdbID { get; set; }
    public string? Type { get; set; }
    public string? Response { get; set; }
    
    // Content properties (used in specific contexts)
    public string? Plot { get; set; }
    
    // Rating properties (used in EnhancedOmdbService)
    public string? Released { get; set; }
    public string? ImdbRating { get; set; }
    
    // Legacy properties maintained for API compatibility
    public List<OmdbRating>? Ratings { get; set; }
}

// Search endpoint response
public class OmdbSearchResponse
{
    public List<OmdbSearchResult>? Search { get; set; }
    public string? TotalResults { get; set; }
    public string? Response { get; set; }
}

public class OmdbSearchResult
{
    public string? Title { get; set; }
    public string? Year { get; set; }
    public string? ImdbID { get; set; }
    public string? Type { get; set; }
    public string? Poster { get; set; }
}

// Movie endpoint response
public class OmdbMovieResponse : OmdbBaseResponse
{
    // Movie-specific properties removed for optimization
    // Removed: DVD, BoxOffice, Production, Website (unused in application)
}

// Series endpoint response
public class OmdbSeriesResponse : OmdbBaseResponse
{
    public string? TotalSeasons { get; set; }
}

// Episode endpoint response
public class OmdbEpisodeResponse : OmdbBaseResponse
{
    public string? Season { get; set; }
    public string? Episode { get; set; }
    public string? Series { get; set; }
    // Removed unused properties: DVD, BoxOffice, Production, Website
}

// Season endpoint response (when requesting specific season)
public class OmdbSeasonResponse
{
    public string? Title { get; set; }
    public string? Season { get; set; }
    public string? TotalSeasons { get; set; }
    public List<OmdbSeasonEpisode>? Episodes { get; set; }
    public string? Response { get; set; }
}

// Season info endpoint response (when requesting series info with season details)
public class OmdbSeasonInfoResponse : OmdbBaseResponse
{
    public string? TotalSeasons { get; set; }
}

public class OmdbSeasonEpisode
{
    public string? Title { get; set; }
    public string? Released { get; set; }
    public string? Episode { get; set; }
    
    [JsonPropertyName("imdbRating")]
    public string? ImdbRating { get; set; }
    
    [JsonPropertyName("imdbID")]
    public string? ImdbID { get; set; }
}

public class OmdbRating
{
    public string? Source { get; set; }
    public string? Value { get; set; }
}



