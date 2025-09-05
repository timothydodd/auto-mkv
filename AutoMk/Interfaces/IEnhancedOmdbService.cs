using System.Threading.Tasks;
using AutoMk.Models;

namespace AutoMk.Interfaces;

public interface IEnhancedOmdbService
{
    Task<CachedEpisodeInfo?> GetEpisodeInfoAsync(string seriesTitle, int season, int episode);
    Task<SeasonInfoCache?> GetOrFetchSeasonInfoAsync(string seriesTitle, int season);
}
