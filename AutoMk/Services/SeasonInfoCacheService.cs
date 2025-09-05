using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AutoMk.Interfaces;
using AutoMk.Models;
using AutoMk.Utilities;
using Microsoft.Extensions.Logging;

namespace AutoMk.Services;

public class SeasonInfoCacheService : ISeasonInfoCacheService
{
    private readonly ILogger<SeasonInfoCacheService> _logger;
    private readonly string _cachePath;
    private readonly Dictionary<string, SeasonInfoCache> _memoryCache;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromDays(30); // Cache expires after 30 days

    public SeasonInfoCacheService(ILogger<SeasonInfoCacheService> logger, RipSettings ripSettings)
    {
        _logger = ValidationHelper.ValidateNotNull(logger);
        var ripSettingsRef = ValidationHelper.ValidateNotNull(ripSettings);
        
        // Use the same directory as MediaStateManager
        var stateDirectory = ripSettingsRef.MediaStateDirectory ?? Path.Combine(AppContext.BaseDirectory, "state");
        _cachePath = Path.Combine(stateDirectory, "season_info_cache.json");
        _memoryCache = new Dictionary<string, SeasonInfoCache>();
        LoadCacheFromDisk();
    }

    public async Task<SeasonInfoCache?> GetSeasonInfoAsync(string seriesTitle, int season)
    {
        var cacheKey = GetCacheKey(seriesTitle, season);
        
        // Check memory cache first
        if (_memoryCache.TryGetValue(cacheKey, out var cachedSeason))
        {
            // Check if cache is still valid
            if (DateTime.Now <= cachedSeason.ExpiryDate)
            {
                _logger.LogDebug($"Found valid cached season info for {cachedSeason.SeriesTitle} S{season:D2}");
                return cachedSeason;
            }
            else
            {
                // Cache expired, remove it
                _logger.LogInformation($"Cache expired for {cachedSeason.SeriesTitle} S{season:D2}, removing from cache");
                _memoryCache.Remove(cacheKey);
                await SaveCacheToDiskAsync();
            }
        }

        _logger.LogDebug($"No valid cached season info found for series {seriesTitle} season {season}");
        return null;
    }

    public async Task CacheSeasonInfoAsync(SeasonInfoCache seasonInfo)
    {
        if (seasonInfo == null || string.IsNullOrEmpty(seasonInfo.SeriesTitle))
        {
            _logger.LogWarning("Cannot cache null or invalid season info");
            return;
        }

        var cacheKey = GetCacheKey(seasonInfo.SeriesTitle, seasonInfo.Season);
        
        // Set cache dates
        seasonInfo.CachedDate = DateTime.Now;
        seasonInfo.ExpiryDate = DateTime.Now.Add(_cacheExpiration);

        // Update memory cache
        _memoryCache[cacheKey] = seasonInfo;
        
        // Save to disk
        await SaveCacheToDiskAsync();
        
        _logger.LogInformation($"Cached season info for {seasonInfo.SeriesTitle} S{seasonInfo.Season:D2} with {seasonInfo.Episodes.Count} episodes");
    }

    public async Task<CachedEpisodeInfo?> GetEpisodeInfoAsync(string seriesTitle, int season, int episode)
    {
        var seasonCache = await GetSeasonInfoAsync(seriesTitle, season);
        if (seasonCache == null)
        {
            return null;
        }

        var episodeInfo = seasonCache.Episodes.FirstOrDefault(e => e.EpisodeNumber == episode);
        if (episodeInfo != null)
        {
            _logger.LogDebug($"Found cached episode info for {seasonCache.SeriesTitle} S{season:D2}E{episode:D2}: {episodeInfo.Title}");
        }

        return episodeInfo;
    }

    public async Task ClearExpiredCacheAsync()
    {
        var expiredKeys = _memoryCache
            .Where(kvp => DateTime.Now > kvp.Value.ExpiryDate)
            .Select(kvp => kvp.Key)
            .ToList();

        if (expiredKeys.Any())
        {
            foreach (var key in expiredKeys)
            {
                var expiredSeason = _memoryCache[key];
                _memoryCache.Remove(key);
                _logger.LogInformation($"Removed expired cache for {expiredSeason.SeriesTitle} S{expiredSeason.Season:D2}");
            }

            await SaveCacheToDiskAsync();
            _logger.LogInformation($"Cleared {expiredKeys.Count} expired season cache entries");
        }
    }


    private string GetCacheKey(string seriesTitle, int season)
    {
        return $"{seriesTitle}_S{season:D2}";
    }

    private void LoadCacheFromDisk()
    {
        try
        {
            if (File.Exists(_cachePath))
            {
                var json = File.ReadAllText(_cachePath);
                var container = JsonSerializer.Deserialize<SeasonCacheContainer>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
                if (container?.CachedSeasons != null)
                {
                    // Load into memory cache, filtering out expired entries
                    var validSeasons = container.CachedSeasons
                        .Where(s => DateTime.Now <= s.ExpiryDate)
                        .ToList();

                    foreach (var season in validSeasons)
                    {
                        var cacheKey = GetCacheKey(season.SeriesTitle, season.Season);
                        _memoryCache[cacheKey] = season;
                    }

                    var expiredCount = container.CachedSeasons.Count - validSeasons.Count;
                    _logger.LogInformation($"Loaded {validSeasons.Count} valid season cache entries from disk");
                    
                    if (expiredCount > 0)
                    {
                        _logger.LogInformation($"Filtered out {expiredCount} expired season cache entries");
                        // Save back to disk without expired entries
                        _ = Task.Run(SaveCacheToDiskAsync);
                    }
                }
            }
            else
            {
                _logger.LogInformation("No existing season cache file found");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading season cache from disk");
            _memoryCache.Clear();
        }
    }

    private async Task SaveCacheToDiskAsync()
    {
        try
        {
            // Ensure directory exists
            FileSystemHelper.EnsureFileDirectoryExists(_cachePath);

            var container = new SeasonCacheContainer
            {
                CachedSeasons = _memoryCache.Values.ToList()
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(container, options);
            await File.WriteAllTextAsync(_cachePath, json);
            
            _logger.LogDebug($"Saved {container.CachedSeasons.Count} season cache entries to disk");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving season cache to disk");
        }
    }

    public void Dispose()
    {
        // Save any pending changes to disk
        SaveCacheToDiskAsync().GetAwaiter().GetResult();
    }
}