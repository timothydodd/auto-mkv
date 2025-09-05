namespace AutoMk.Models;

/// <summary>
/// Extended media information for detailed operations.
/// Contains plot information and series-specific data.
/// </summary>
public class MediaDetails
{
    /// <summary>
    /// Core identification information
    /// </summary>
    public MediaIdentity Identity { get; set; } = new();
    
    /// <summary>
    /// Plot summary/description
    /// </summary>
    public string? Plot { get; set; }
    
    /// <summary>
    /// Total number of seasons (for TV series only)
    /// </summary>
    public string? TotalSeasons { get; set; }
    
    
    /// <summary>
    /// Creates MediaDetails from an OmdbSeriesResponse
    /// </summary>
    public static MediaDetails FromSeriesResponse(OmdbSeriesResponse seriesResponse)
    {
        return new MediaDetails
        {
            Identity = new MediaIdentity
            {
                Title = seriesResponse.Title,
                Type = seriesResponse.Type,
                Year = seriesResponse.Year,
                ImdbID = seriesResponse.ImdbID,
                Response = seriesResponse.Response
            },
            Plot = seriesResponse.Plot,
            TotalSeasons = seriesResponse.TotalSeasons
        };
    }
    
    /// <summary>
    /// Creates MediaDetails from an OmdbMovieResponse
    /// </summary>
    public static MediaDetails FromMovieResponse(OmdbMovieResponse movieResponse)
    {
        return new MediaDetails
        {
            Identity = new MediaIdentity
            {
                Title = movieResponse.Title,
                Type = movieResponse.Type,
                Year = movieResponse.Year,
                ImdbID = movieResponse.ImdbID,
                Response = movieResponse.Response
            },
            Plot = movieResponse.Plot,
            TotalSeasons = null // Movies don't have seasons
        };
    }
}