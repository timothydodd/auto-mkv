namespace AutoMk.Models;

/// <summary>
/// Episode-specific information for TV series episodes.
/// Contains episode number, season, and series information.
/// </summary>
public class EpisodeDetails
{
    /// <summary>
    /// Core identification information for the episode
    /// </summary>
    public MediaIdentity Identity { get; set; } = new();
    
    /// <summary>
    /// Season number
    /// </summary>
    public string? Season { get; set; }
    
    /// <summary>
    /// Episode number within the season
    /// </summary>
    public string? Episode { get; set; }
    
    /// <summary>
    /// The series title (parent series)
    /// </summary>
    public string? Series { get; set; }
    
    /// <summary>
    /// Episode plot/description
    /// </summary>
    public string? Plot { get; set; }
    
    
    /// <summary>
    /// Creates EpisodeDetails from an OmdbEpisodeResponse
    /// </summary>
    public static EpisodeDetails FromEpisodeResponse(OmdbEpisodeResponse episodeResponse)
    {
        return new EpisodeDetails
        {
            Identity = new MediaIdentity
            {
                Title = episodeResponse.Title,
                Type = episodeResponse.Type,
                Year = episodeResponse.Year,
                ImdbID = episodeResponse.ImdbID,
                Response = episodeResponse.Response
            },
            Season = episodeResponse.Season,
            Episode = episodeResponse.Episode,
            Series = episodeResponse.Series,
            Plot = episodeResponse.Plot
        };
    }
    
    /// <summary>
    /// Creates EpisodeDetails from cached episode information
    /// </summary>
    public static EpisodeDetails FromCachedEpisodeInfo(CachedEpisodeInfo cachedInfo, string seriesTitle, int season)
    {
        return new EpisodeDetails
        {
            Identity = new MediaIdentity
            {
                Title = cachedInfo.Title,
                Type = "episode",
                ImdbID = cachedInfo.ImdbId,
                Response = "True"
            },
            Season = season.ToString(),
            Episode = cachedInfo.EpisodeNumber.ToString(),
            Series = seriesTitle,
            Plot = cachedInfo.Plot
        };
    }
}