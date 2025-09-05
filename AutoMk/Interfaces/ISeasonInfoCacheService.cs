using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMk.Models;

namespace AutoMk.Interfaces;

public interface ISeasonInfoCacheService
{
    Task<SeasonInfoCache?> GetSeasonInfoAsync(string seriesTitle, int season);
    Task CacheSeasonInfoAsync(SeasonInfoCache seasonInfo);
    Task<CachedEpisodeInfo?> GetEpisodeInfoAsync(string seriesTitle, int season, int episode);
    Task ClearExpiredCacheAsync();
}