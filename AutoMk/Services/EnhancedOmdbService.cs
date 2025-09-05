using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AutoMk.Extensions;
using AutoMk.Interfaces;
using AutoMk.Models;
using AutoMk.Utilities;
using Microsoft.Extensions.Logging;

namespace AutoMk.Services;

public class EnhancedOmdbService : IEnhancedOmdbService
{
    private readonly IOmdbClient _omdbClient;
    private readonly ISeasonInfoCacheService _cacheService;
    private readonly ILogger<EnhancedOmdbService> _logger;

    public EnhancedOmdbService(
        IOmdbClient omdbClient,
        ISeasonInfoCacheService cacheService,
        ILogger<EnhancedOmdbService> logger)
    {
        _omdbClient = ValidationHelper.ValidateNotNull(omdbClient);
        _cacheService = ValidationHelper.ValidateNotNull(cacheService);
        _logger = ValidationHelper.ValidateNotNull(logger);
    }

    public async Task<CachedEpisodeInfo?> GetEpisodeInfoAsync(string seriesTitle, int season, int episode)
    {
        // Use GetOrFetchSeasonInfoAsync to get the season data (from cache or API)
        var seasonCache = await GetOrFetchSeasonInfoAsync(seriesTitle, season);

        if (seasonCache != null)
        {
            // Find the episode in the season data
            var episodeFromSeason = seasonCache.Episodes.FirstOrDefault(e => e.EpisodeNumber == episode);
            if (episodeFromSeason != null)
            {
                _logger.LogDebug($"Retrieved episode from season cache for {seriesTitle} S{season:D2}E{episode:D2}: {episodeFromSeason.Title}");
                return episodeFromSeason;
            }
            else
            {
                _logger.LogWarning($"Episode {episode} not found in season {season} for {seriesTitle} (season has {seasonCache.Episodes.Count} episodes)");

                // If requesting episode beyond what's in the season, return null
                if (episode > seasonCache.Episodes.Count)
                {
                    return null;
                }
            }
        }

        // Fallback to individual episode lookup if season fetch failed or episode not found
        _logger.LogInformation($"Falling back to individual episode lookup for {seriesTitle} S{season:D2}E{episode:D2}");

        _logger.LogWarning($"Failed to retrieve episode info for {seriesTitle} S{season:D2}E{episode:D2}");
        return null;
    }

    public async Task<SeasonInfoCache?> GetOrFetchSeasonInfoAsync(string seriesTitle, int season)
    {

        // Check cache first
        var cachedSeason = await _cacheService.GetSeasonInfoAsync(seriesTitle, season);
        if (cachedSeason != null)
        {
            _logger.LogDebug($"Retrieved cached season info for {seriesTitle} S{season:D2}");
            return cachedSeason;
        }

        // Fetch from OMDB
        _logger.LogInformation($"Fetching season {season} info for {seriesTitle} from OMDB API");
        var seasonData = await _omdbClient.GetSeasonInfo(seriesTitle, season);

        if (seasonData.IsValidOmdbResponse() &&
            seasonData.Episodes?.Any() == true)
        {
            _logger.LogDebug($"Season data received: Title={seasonData.Title}, Season={seasonData.Season}, Episodes={seasonData.Episodes.Count}");

            var seasonCache = new SeasonInfoCache
            {
                SeriesTitle = seriesTitle,
                Season = season,
                Episodes = seasonData.Episodes.Select(ConvertOmdbSeasonEpisodeToCachedEpisodeInfo).ToList()
            };

            _logger.LogDebug($"Created season cache: SeriesTitle={seasonCache.SeriesTitle}, Episodes={seasonCache.Episodes.Count}");

            // Always cache using series title
            await _cacheService.CacheSeasonInfoAsync(seasonCache);
            _logger.LogInformation($"Cached season {season} for {seriesTitle} with {seasonCache.Episodes.Count} episodes");

            return seasonCache;
        }

        _logger.LogWarning($"Failed to fetch season {season} info for {seriesTitle} from OMDB API");
        return null;
    }


    private static CachedEpisodeInfo ConvertOmdbSeasonEpisodeToCachedEpisodeInfo(OmdbSeasonEpisode omdbEpisode)
    {
        var episodeInfo = new CachedEpisodeInfo
        {
            Title = omdbEpisode.Title ?? "Unknown Episode",
            ImdbId = string.Empty
        };

        // Parse episode number
        if (int.TryParse(omdbEpisode.Episode, out var episodeNumber))
        {
            episodeInfo.EpisodeNumber = episodeNumber;
        }

        // Parse release date
        if (DateTime.TryParseExact(omdbEpisode.Released, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var releaseDate))
        {
            episodeInfo.Released = releaseDate;
        }

        // Parse IMDb rating
        if (double.TryParse(omdbEpisode.ImdbRating, out var rating))
        {
            episodeInfo.ImdbRating = rating;
        }

        return episodeInfo;
    }

    private static CachedEpisodeInfo ConvertOmdbEpisodeResponseToCachedEpisodeInfo(OmdbEpisodeResponse omdbEpisode, int episodeNumber)
    {
        var episodeInfo = new CachedEpisodeInfo
        {
            EpisodeNumber = episodeNumber,
            Title = omdbEpisode.Title ?? "Unknown Episode",
            ImdbId = string.Empty,
            Plot = omdbEpisode.Plot
        };

        // Parse release date
        if (DateTime.TryParseExact(omdbEpisode.Released, "dd MMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var releaseDate))
        {
            episodeInfo.Released = releaseDate;
        }

        // Parse IMDb rating
        if (double.TryParse(omdbEpisode.ImdbRating, out var rating))
        {
            episodeInfo.ImdbRating = rating;
        }

        return episodeInfo;
    }

}
