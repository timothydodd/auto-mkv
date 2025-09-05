using System;
using System.Linq;
using AutoMk.Models;

namespace AutoMk.Utilities;

/// <summary>
/// Centralized utility class for converting between optimized models.
/// Provides consistent conversion logic and reduces scattered conversion code throughout services.
/// </summary>
public static class ModelConverter
{
    #region MediaIdentity Conversions
    
    /// <summary>
    /// Converts OmdbMovieResponse to MediaIdentity
    /// </summary>
    public static MediaIdentity ToMediaIdentity(OmdbMovieResponse movieResponse)
    {
        return new MediaIdentity
        {
            Title = movieResponse.Title,
            Year = movieResponse.Year,
            ImdbID = movieResponse.ImdbID,
            Type = movieResponse.Type,
            Response = movieResponse.Response
        };
    }
    
    /// <summary>
    /// Converts OmdbSeriesResponse to MediaIdentity
    /// </summary>
    public static MediaIdentity ToMediaIdentity(OmdbSeriesResponse seriesResponse)
    {
        return new MediaIdentity
        {
            Title = seriesResponse.Title,
            Year = seriesResponse.Year,
            ImdbID = seriesResponse.ImdbID,
            Type = seriesResponse.Type,
            Response = seriesResponse.Response
        };
    }
    
    /// <summary>
    /// Converts OmdbSearchResult to MediaIdentity
    /// </summary>
    public static MediaIdentity ToMediaIdentity(OmdbSearchResult searchResult)
    {
        return MediaIdentity.FromSearchResult(searchResult);
    }
    
    /// <summary>
    /// Converts OmdbEpisodeResponse to MediaIdentity
    /// </summary>
    public static MediaIdentity ToMediaIdentity(OmdbEpisodeResponse episodeResponse)
    {
        return new MediaIdentity
        {
            Title = episodeResponse.Title,
            Year = episodeResponse.Year,
            ImdbID = episodeResponse.ImdbID,
            Type = episodeResponse.Type,
            Response = episodeResponse.Response
        };
    }
    
    #endregion
    
    #region MediaDetails Conversions
    
    /// <summary>
    /// Converts OmdbMovieResponse to MediaDetails
    /// </summary>
    public static MediaDetails ToMediaDetails(OmdbMovieResponse movieResponse)
    {
        var identity = ToMediaIdentity(movieResponse);
        return new MediaDetails
        {
            Identity = identity,
            Plot = movieResponse.Plot
        };
    }
    
    /// <summary>
    /// Converts OmdbSeriesResponse to MediaDetails
    /// </summary>
    public static MediaDetails ToMediaDetails(OmdbSeriesResponse seriesResponse)
    {
        var identity = ToMediaIdentity(seriesResponse);
        return new MediaDetails
        {
            Identity = identity,
            Plot = seriesResponse.Plot,
            TotalSeasons = seriesResponse.TotalSeasons
        };
    }
    
    #endregion
    
    #region EpisodeDetails Conversions
    
    /// <summary>
    /// Converts OmdbEpisodeResponse to EpisodeDetails
    /// </summary>
    public static EpisodeDetails ToEpisodeDetails(OmdbEpisodeResponse episodeResponse)
    {
        var identity = ToMediaIdentity(episodeResponse);
        return new EpisodeDetails
        {
            Identity = identity,
            Plot = episodeResponse.Plot,
            Season = episodeResponse.Season,
            Episode = episodeResponse.Episode,
            Series = episodeResponse.Series
        };
    }
    
    #endregion
    
    #region SearchResult Conversions
    
    /// <summary>
    /// Converts OptimizedSearchResult to MediaIdentity
    /// </summary>
    public static MediaIdentity ToMediaIdentity(OptimizedSearchResult searchResult)
    {
        return searchResult.ToMediaIdentity();
    }
    
    
    /// <summary>
    /// Converts OmdbSearchResult to OptimizedSearchResult
    /// </summary>
    public static OptimizedSearchResult ToOptimizedSearchResult(OmdbSearchResult omdbSearchResult)
    {
        return OptimizedSearchResult.FromOmdbSearchResult(omdbSearchResult);
    }
    
    /// <summary>
    /// Converts multiple OmdbSearchResults to OptimizedSearchResults
    /// </summary>
    public static OptimizedSearchResult[] ToOptimizedSearchResults(OmdbSearchResult[] omdbSearchResults)
    {
        return omdbSearchResults?.Select(ToOptimizedSearchResult).ToArray() ?? Array.Empty<OptimizedSearchResult>();
    }
    
    /// <summary>
    /// Converts OmdbSeriesResponse to OptimizedSearchResult
    /// </summary>
    public static OptimizedSearchResult ToOptimizedSearchResult(OmdbSeriesResponse seriesResponse)
    {
        return new OptimizedSearchResult
        {
            Title = seriesResponse.Title,
            Year = seriesResponse.Year,
            Type = seriesResponse.Type,
            ImdbID = seriesResponse.ImdbID,
            Poster = null // API responses don't have poster URLs
        };
    }
    
    /// <summary>
    /// Converts OmdbMovieResponse to OptimizedSearchResult
    /// </summary>
    public static OptimizedSearchResult ToOptimizedSearchResult(OmdbMovieResponse movieResponse)
    {
        return new OptimizedSearchResult
        {
            Title = movieResponse.Title,
            Year = movieResponse.Year,
            Type = movieResponse.Type,
            ImdbID = movieResponse.ImdbID,
            Poster = null // API responses don't have poster URLs
        };
    }
    
    #endregion
    
    #region MovieInfo Conversions
    
    /// <summary>
    /// Converts MovieInfo to MediaIdentity
    /// </summary>
    public static MediaIdentity ToMediaIdentity(MovieInfo movieInfo)
    {
        return movieInfo.ToMediaIdentity();
    }
    
    
    /// <summary>
    /// Converts OmdbMovieResponse to MovieInfo
    /// </summary>
    public static MovieInfo ToMovieInfo(OmdbMovieResponse movieResponse)
    {
        return MovieInfo.FromMovieResponse(movieResponse);
    }
    
    #endregion
    
    #region SeriesInfo Conversions
    
    /// <summary>
    /// Converts SeriesInfo to MediaIdentity
    /// </summary>
    public static MediaIdentity ToMediaIdentity(SeriesInfo seriesInfo)
    {
        return seriesInfo.ToMediaIdentity();
    }
    
    /// <summary>
    /// Converts SeriesInfo to MediaDetails
    /// </summary>
    public static MediaDetails ToMediaDetails(SeriesInfo seriesInfo, string? plot = null)
    {
        return seriesInfo.ToMediaDetails(plot);
    }
    
    
    /// <summary>
    /// Converts OmdbSeriesResponse to SeriesInfo
    /// </summary>
    public static SeriesInfo ToSeriesInfo(OmdbSeriesResponse seriesResponse)
    {
        return SeriesInfo.FromSeriesResponse(seriesResponse);
    }
    
    #endregion
    
    #region ConfirmationInfo Conversions
    
    /// <summary>
    /// Converts ConfirmationInfo to MediaIdentity
    /// </summary>
    public static MediaIdentity ToMediaIdentity(ConfirmationInfo confirmationInfo)
    {
        return confirmationInfo.ToMediaIdentity();
    }
    
    /// <summary>
    /// Converts OmdbMovieResponse to ConfirmationInfo
    /// </summary>
    public static ConfirmationInfo ToConfirmationInfo(OmdbMovieResponse movieResponse)
    {
        return new ConfirmationInfo
        {
            Title = movieResponse.Title,
            Year = movieResponse.Year,
            ImdbID = movieResponse.ImdbID,
            Type = movieResponse.Type,
            Plot = movieResponse.Plot,
            Response = movieResponse.Response
        };
    }
    
    /// <summary>
    /// Converts OmdbSeriesResponse to ConfirmationInfo
    /// </summary>
    public static ConfirmationInfo ToConfirmationInfo(OmdbSeriesResponse seriesResponse)
    {
        return new ConfirmationInfo
        {
            Title = seriesResponse.Title,
            Year = seriesResponse.Year,
            ImdbID = seriesResponse.ImdbID,
            Type = seriesResponse.Type,
            Plot = seriesResponse.Plot,
            Response = seriesResponse.Response
        };
    }
    
    /// <summary>
    /// Converts MediaDetails to ConfirmationInfo
    /// </summary>
    public static ConfirmationInfo ToConfirmationInfo(MediaDetails details)
    {
        return ConfirmationInfo.FromMediaDetails(details);
    }
    
    /// <summary>
    /// Converts EpisodeDetails to ConfirmationInfo
    /// </summary>
    public static ConfirmationInfo ToConfirmationInfo(EpisodeDetails episode)
    {
        return ConfirmationInfo.FromEpisodeDetails(episode);
    }
    
    #endregion
    
    #region Legacy CachedMediaInfo Conversions
    
    /// <summary>
    /// Converts CachedMediaInfo to MediaIdentity
    /// </summary>
    public static MediaIdentity ToMediaIdentity(CachedMediaInfo cachedInfo)
    {
        return new MediaIdentity
        {
            Title = cachedInfo.Title,
            Year = cachedInfo.Year,
            ImdbID = cachedInfo.ImdbID,
            Type = cachedInfo.Type,
            Response = cachedInfo.Response
        };
    }
    
    /// <summary>
    /// Converts MediaIdentity to CachedMediaInfo
    /// </summary>
    public static CachedMediaInfo ToCachedMediaInfo(MediaIdentity identity)
    {
        return new CachedMediaInfo
        {
            Title = identity.Title,
            Year = identity.Year,
            ImdbID = identity.ImdbID,
            Type = identity.Type,
            Response = identity.Response ?? "True"
        };
    }
    
    #endregion
    
    #region Bulk Conversion Methods
    
    
    /// <summary>
    /// Converts an array of OptimizedSearchResult to MediaIdentity array
    /// </summary>
    public static MediaIdentity[] ToMediaIdentities(OptimizedSearchResult[] searchResults)
    {
        return searchResults?.Select(ToMediaIdentity).ToArray() ?? Array.Empty<MediaIdentity>();
    }
    
    #endregion
    
    #region Validation Helpers
    
    /// <summary>
    /// Checks if any model has valid identification data
    /// </summary>
    public static bool IsValidIdentification(object model)
    {
        return model switch
        {
            MediaIdentity identity => identity.IsValid(),
            OptimizedSearchResult searchResult => searchResult.IsValid(),
            MovieInfo movieInfo => movieInfo.IsValid(),
            SeriesInfo seriesInfo => seriesInfo.IsValid(),
            ConfirmationInfo confirmationInfo => confirmationInfo.IsValid(),
            _ => false
        };
    }
    
    #endregion
}