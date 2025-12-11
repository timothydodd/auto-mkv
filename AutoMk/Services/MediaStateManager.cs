using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using AutoMk.Extensions;
using AutoMk.Interfaces;
using AutoMk.Models;
using AutoMk.Utilities;
using Microsoft.Extensions.Logging;

namespace AutoMk.Services;

public class MediaStateManager : IMediaStateManager
{
    private readonly string _stateFilePath;
    private readonly ILogger<MediaStateManager> _logger;
    private readonly IOmdbClient _omdbClient;
    private MediaStateContainer? _cachedContainer;

    public MediaStateManager(RipSettings ripSettings, ILogger<MediaStateManager> logger, IOmdbClient omdbClient)
    {
        if (ripSettings.MediaStateDirectory == null)
        {
            ripSettings.MediaStateDirectory = Path.Combine(AppContext.BaseDirectory, "state");
        }
        _stateFilePath = Path.Combine(ripSettings.MediaStateDirectory, "media_state.json");
        _logger = ValidationHelper.ValidateNotNull(logger);
        _omdbClient = ValidationHelper.ValidateNotNull(omdbClient);

        // Ensure state directory exists
        FileSystemHelper.EnsureDirectoryExists(ripSettings.MediaStateDirectory);
    }

    public async Task<SeriesState> GetOrCreateSeriesStateAsync(string seriesTitle)
    {
        var allStates = await LoadAllStatesAsync();

        var existingState = allStates?.FirstOrDefault(s => s != null &&
            (s.SeriesTitle != null && s.SeriesTitle.Equals(seriesTitle, StringComparison.OrdinalIgnoreCase)));

        if (existingState != null)
        {
            return existingState;
        }

        var newState = new SeriesState
        {
            SeriesTitle = seriesTitle,
            CurrentSeason = 1,
            NextEpisode = 1,
            ProcessedDiscs = new List<DiscInfo>()
        };

        return newState;
    }

    public DiscInfo GetNextDiscInfo(SeriesState seriesState, string discName, int trackCount, ParsedDiscInfo? parsedInfo = null, bool useAutoIncrement = false, List<AkTitle>? rippedTracks = null)
    {
        int season;
        int startingEpisode;

        // Check if user chose to use AutoIncrement for this disc
        if (useAutoIncrement || (seriesState.AutoIncrement && HasSimilarDiscPattern(seriesState, discName)))
        {
            // When using auto increment, check for identical disc patterns
            if (useAutoIncrement && rippedTracks != null)
            {
                var discFingerprint = CreateDiscFingerprint(discName, rippedTracks);
                var existingPattern = seriesState.KnownDiscPatterns.FirstOrDefault(p =>
                    p.DiscTitle.Equals(discFingerprint.DiscTitle, StringComparison.OrdinalIgnoreCase) &&
                    p.TrackCount == discFingerprint.TrackCount);

                if (existingPattern != null)
                {
                    // This is a repeat of an identical disc - assign new sequence number
                    var sequenceNumber = seriesState.KnownDiscPatterns
                        .Where(p => p.DiscTitle.Equals(discFingerprint.DiscTitle, StringComparison.OrdinalIgnoreCase) &&
                                   p.TrackCount == discFingerprint.TrackCount)
                        .Max(p => p.SequenceNumber) + 1;

                    _logger.LogInformation($"Auto Increment: Detected identical disc pattern '{discFingerprint.DiscTitle}' (sequence #{sequenceNumber})");
                }
            }

            // Use auto-increment logic - continue from where we left off
            season = seriesState.CurrentSeason;
            startingEpisode = seriesState.NextEpisode;
            _logger.LogInformation($"Auto Increment {(useAutoIncrement ? "chosen by user" : "enabled")} for {seriesState.SeriesTitle}: Starting at S{season:D2}E{startingEpisode:D2}");
        }
        else if (parsedInfo != null && parsedInfo.Season > 0)
        {
            // Check if this disc has been processed before (only when not using auto increment)
            var existingDisc = seriesState.ProcessedDiscs
                .FirstOrDefault(d => d.DiscName.Equals(discName, StringComparison.OrdinalIgnoreCase));

            if (existingDisc != null)
            {
                _logger.LogWarning($"Disc {discName} has already been processed for {seriesState.SeriesTitle}");
                return existingDisc;
            }

            // Use parsed season information
            season = parsedInfo.Season;

            // Calculate starting episode based on this disc in the season
            var existingDiscsInSeason = seriesState.ProcessedDiscs
                .Where(d => d.Season == season)
                .OrderBy(d => d.DiscNumber)
                .ToList();

            if (existingDiscsInSeason.Any())
            {
                // Find the next episode after the last processed disc in this season
                var lastDisc = existingDiscsInSeason.Last();
                if (lastDisc.TrackToEpisodeMapping?.Any() == true)
                {
                    // Use episode mapping to get the highest episode number for this disc
                    var maxEpisodeForDisc = lastDisc.TrackToEpisodeMapping.Values
                        .SelectMany(episodes => episodes)
                        .Max();
                    startingEpisode = maxEpisodeForDisc + 1;
                }
                else
                {
                    // Fallback to episode count
                    startingEpisode = lastDisc.StartingEpisode + lastDisc.EpisodeCount;
                }
            }
            else
            {
                // First disc of this season
                startingEpisode = 1;
            }

            _logger.LogInformation($"Season {season}, Disc {parsedInfo.DiscNumber}: Starting at episode {startingEpisode}");
        }
        else
        {
            // Check if this disc has been processed before (only when not using auto increment)
            var existingDisc = seriesState.ProcessedDiscs
                .FirstOrDefault(d => d.DiscName.Equals(discName, StringComparison.OrdinalIgnoreCase));

            if (existingDisc != null)
            {
                _logger.LogWarning($"Disc {discName} has already been processed for {seriesState.SeriesTitle}");
                return existingDisc;
            }

            // Fallback to sequential processing
            season = seriesState.CurrentSeason;
            startingEpisode = seriesState.NextEpisode;
        }

        // Determine disc number based on mode
        int discNumber;
        if (useAutoIncrement)
        {
            // Use auto-incrementing disc number
            discNumber = seriesState.NextDiscNumber;
            _logger.LogInformation($"Auto Increment: Using disc number {discNumber} for {seriesState.SeriesTitle}");
        }
        else
        {
            // Use parsed disc number or default to 1
            discNumber = parsedInfo?.DiscNumber ?? 1;
        }

        var discInfo = new DiscInfo
        {
            DiscName = discName,
            Season = season,
            DiscNumber = discNumber,
            StartingEpisode = startingEpisode,
            TrackCount = trackCount,
            ProcessedDate = DateTime.UtcNow
        };

        return discInfo;
    }

    public async Task UpdateSeriesStateAsync(SeriesState seriesState, DiscInfo discInfo, int actualEpisodeCount, List<AkTitle>? rippedTracks = null, bool wasAutoIncrement = false)
    {
        // Update the disc info with actual episode count
        discInfo.EpisodeCount = actualEpisodeCount;

        // Check if disc already exists and update it
        var existingDiscIndex = seriesState.ProcessedDiscs.FindIndex(d => d.DiscName.Equals(discInfo.DiscName, StringComparison.OrdinalIgnoreCase));
        if (existingDiscIndex >= 0)
        {
            // Update existing disc info with new mapping
            seriesState.ProcessedDiscs[existingDiscIndex] = discInfo;
        }
        else
        {
            // Add new disc
            seriesState.ProcessedDiscs.Add(discInfo);
        }

        // Record disc pattern if auto increment was used
        if (wasAutoIncrement && rippedTracks != null)
        {
            var discFingerprint = CreateDiscFingerprint(discInfo.DiscName, rippedTracks);

            // Find existing pattern with same title and track count
            var existingPatterns = seriesState.KnownDiscPatterns
                .Where(p => p.DiscTitle.Equals(discFingerprint.DiscTitle, StringComparison.OrdinalIgnoreCase) &&
                           p.TrackCount == discFingerprint.TrackCount)
                .ToList();

            // Determine sequence number
            var sequenceNumber = existingPatterns.Any() ? existingPatterns.Max(p => p.SequenceNumber) + 1 : 1;

            // Create and add the new pattern
            var newPattern = new DiscPattern
            {
                DiscTitle = discFingerprint.DiscTitle,
                TrackCount = discFingerprint.TrackCount,
                SequenceNumber = sequenceNumber,
                AssignedSeason = discInfo.Season,
                StartingEpisode = discInfo.StartingEpisode,
                EpisodeCount = actualEpisodeCount,
                ProcessedDate = DateTime.UtcNow
            };

            seriesState.KnownDiscPatterns.Add(newPattern);
            _logger.LogInformation($"Recorded disc pattern: '{newPattern.DiscTitle}' (tracks: {newPattern.TrackCount}, sequence: {sequenceNumber}, season: {discInfo.Season}, episodes: {discInfo.StartingEpisode}-{discInfo.StartingEpisode + actualEpisodeCount - 1})");
        }

        // Update current season to the highest season we've processed
        var highestSeason = seriesState.ProcessedDiscs.Max(d => d.Season);
        seriesState.CurrentSeason = Math.Max(seriesState.CurrentSeason, highestSeason);

        // Update next episode number using the episode mappings
        var allProcessedEpisodes = seriesState.ProcessedDiscs
            .SelectMany(d =>
            {
                if (d.TrackToEpisodeMapping?.Any() == true)
                {
                    // Use the mapping to get all episode numbers
                    return d.TrackToEpisodeMapping.Values
                        .SelectMany(episodes => episodes)
                        .Select(ep => new { Season = d.Season, Episode = ep });
                }
                else
                {
                    // Fallback to range-based calculation using episode count
                    return Enumerable.Range(d.StartingEpisode, d.EpisodeCount)
                        .Select(ep => new { Season = d.Season, Episode = ep });
                }
            })
            .GroupBy(x => x.Season)
            .ToDictionary(g => g.Key, g => g.Max(x => x.Episode));

        if (allProcessedEpisodes.Any())
        {
            var currentSeasonMaxEpisode = allProcessedEpisodes.ContainsKey(seriesState.CurrentSeason)
                ? allProcessedEpisodes[seriesState.CurrentSeason]
                : 0;

            // Handle auto-increment season transition logic
            if (seriesState.AutoIncrement && currentSeasonMaxEpisode > 0)
            {
                var nextEpisode = currentSeasonMaxEpisode + 1;

                // Get the actual episode count for this season from OMDB
                var seasonEpisodeCount = await GetSeasonEpisodeCountAsync(seriesState, seriesState.CurrentSeason);

                if (seasonEpisodeCount > 0 && nextEpisode > seasonEpisodeCount)
                {
                    seriesState.CurrentSeason++;
                    seriesState.NextEpisode = 1;
                    seriesState.NextDiscNumber = 1; // Reset disc number for new season
                    _logger.LogInformation($"Auto Increment: Moving to next season {seriesState.CurrentSeason} for {seriesState.SeriesTitle} (completed {seasonEpisodeCount} episodes)");
                }
                else
                {
                    seriesState.NextEpisode = nextEpisode;
                }
            }
            else
            {
                seriesState.NextEpisode = currentSeasonMaxEpisode + 1;
            }
        }
        else
        {
            seriesState.NextEpisode = 1;
        }

        // Update next disc number if auto increment is enabled
        if (seriesState.AutoIncrement)
        {
            var maxDiscNumber = seriesState.ProcessedDiscs
                .Where(d => d.Season == seriesState.CurrentSeason)
                .Select(d => d.DiscNumber)
                .DefaultIfEmpty(0)
                .Max();
            seriesState.NextDiscNumber = maxDiscNumber + 1;
        }

        _logger.LogInformation($"Updated series state for {seriesState.SeriesTitle}: Season {seriesState.CurrentSeason}, Next Episode {seriesState.NextEpisode}, Next Disc {seriesState.NextDiscNumber}");
    }

    public async Task SaveSeriesStateAsync(SeriesState seriesState)
    {
        try
        {
            var container = await LoadStateContainerAsync();

            // Remove existing state for this series
            container.SeriesStates.RemoveAll(s => s.SeriesTitle == seriesState.SeriesTitle);

            // Add updated state
            container.SeriesStates.Add(seriesState);

            await SaveStateContainerAsync(container);

            _logger.LogInformation($"Saved state for series: {seriesState.SeriesTitle}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error saving series state for: {seriesState.SeriesTitle}");
        }
    }

    public async Task<ManualIdentification?> GetManualIdentificationAsync(string discName)
    {
        var container = await LoadStateContainerAsync();

        // Extract base name for pattern matching (remove season/disc info)
        var baseName = ExtractBaseNameForMatching(discName);

        // Only return cached identifications for TV series
        // Movies should never use cached identifications - each disc is a new movie
        var match = container.ManualIdentifications.FirstOrDefault(m =>
            m.DiscNamePattern.Equals(baseName, StringComparison.OrdinalIgnoreCase) ||
            discName.StartsWith(m.DiscNamePattern, StringComparison.OrdinalIgnoreCase));

        // If the cached identification is for a movie, don't use it for a different disc
        if (match != null && match.MediaType.Equals("movie", StringComparison.OrdinalIgnoreCase))
        {
            // Only use movie cache if the disc name matches exactly (for re-ripping the same disc)
            if (!match.DiscNamePattern.Equals(baseName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation($"Ignoring cached movie identification for '{match.DiscNamePattern}' - disc '{discName}' is different");
                return null;
            }
        }

        return match;
    }

    public async Task SaveManualIdentificationAsync(string discName, MediaIdentity mediaIdentity)
    {
        try
        {
            var container = await LoadStateContainerAsync();

            // Extract base name for pattern
            var baseName = ExtractBaseNameForMatching(discName);

            // Remove any existing identification for this pattern
            container.ManualIdentifications.RemoveAll(m =>
                m.DiscNamePattern.Equals(baseName, StringComparison.OrdinalIgnoreCase));

            // Add new identification
            var identification = new ManualIdentification
            {
                DiscNamePattern = baseName,
                MediaTitle = mediaIdentity.Title ?? string.Empty,
                ImdbId = mediaIdentity.ImdbID,
                MediaType = mediaIdentity.Type ?? string.Empty,
                Year = mediaIdentity.Year,
                IdentifiedDate = DateTime.UtcNow,
                CachedOmdbData = new CachedMediaInfo
                {
                    Title = mediaIdentity.Title,
                    ImdbID = mediaIdentity.ImdbID,
                    Type = mediaIdentity.Type,
                    Year = mediaIdentity.Year,
                    Response = mediaIdentity.Response ?? "True"
                }
            };

            container.ManualIdentifications.Add(identification);
            await SaveStateContainerAsync(container);

            _logger.LogInformation($"Saved manual identification for pattern '{baseName}' as '{mediaIdentity.Title}'");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving manual identification");
        }
    }

    private async Task<int> GetSeasonEpisodeCountAsync(SeriesState seriesState, int season)
    {
        // Check cache first
        if (seriesState.SeasonEpisodeCounts.TryGetValue(season, out var cachedCount))
        {
            return cachedCount;
        }

        try
        {
            // Fetch from OMDB API
            var seasonData = await _omdbClient.GetSeasonInfo(seriesState.SeriesTitle, season);
            if (seasonData.IsValidOmdbResponse()
                && seasonData.Episodes?.Count > 0)
            {
                var episodeCount = seasonData.Episodes.Count;

                // Cache the result
                seriesState.SeasonEpisodeCounts[season] = episodeCount;

                _logger.LogInformation($"Fetched season {season} episode count for {seriesState.SeriesTitle}: {episodeCount} episodes");
                return episodeCount;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Failed to get season {season} episode count for {seriesState.SeriesTitle}");
        }

        // Return 0 if we can't get the count (will use fallback logic)
        return 0;
    }

    private bool HasSimilarDiscPattern(SeriesState seriesState, string discName)
    {
        // Extract base name without season/disc info
        var baseName = ExtractBaseNameForMatching(discName);

        // Check if any processed disc has a similar base name
        return seriesState.ProcessedDiscs.Any(d =>
        {
            var existingBaseName = ExtractBaseNameForMatching(d.DiscName);
            return existingBaseName.Equals(baseName, StringComparison.OrdinalIgnoreCase);
        });
    }

    private string ExtractBaseNameForMatching(string discName)
    {
        // Remove season/disc indicators to get base name
        var cleaned = discName;

        // Remove patterns like _S##_D##, Season #, Disc #, etc.
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"[_\s]+[Ss]\d+[_\s]+[Dd]\d+.*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"[_\s]+Season[_\s]+\d+.*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"[_\s]+[Ss]\d+.*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Replace underscores with spaces
        cleaned = cleaned.Replace("_", " ");

        return cleaned.Trim();
    }

    private async Task<List<SeriesState>> LoadAllStatesAsync()
    {
        var container = await LoadStateContainerAsync();
        return container.SeriesStates;
    }

    private async Task<MediaStateContainer> LoadStateContainerAsync()
    {
        if (_cachedContainer != null)
            return _cachedContainer;

        try
        {
            if (!File.Exists(_stateFilePath))
            {
                _cachedContainer = new MediaStateContainer();
                return _cachedContainer;
            }

            var json = await File.ReadAllTextAsync(_stateFilePath);

            // Try to deserialize as new container format first
            try
            {
                _cachedContainer = JsonSerializer.Deserialize<MediaStateContainer>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (_cachedContainer == null)
                {
                    _cachedContainer = new MediaStateContainer();
                }
            }
            catch
            {
                // Fall back to old format (list of SeriesState)
                var oldStates = JsonSerializer.Deserialize<List<SeriesState>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<SeriesState>();
                _cachedContainer = new MediaStateContainer { SeriesStates = oldStates };
                // Save in new format
                await SaveStateContainerAsync(_cachedContainer);
            }

            return _cachedContainer;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error loading state container, starting fresh");
            _cachedContainer = new MediaStateContainer();
            return _cachedContainer;
        }
    }

    private async Task SaveStateContainerAsync(MediaStateContainer container)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var json = JsonSerializer.Serialize(container, options);
            await File.WriteAllTextAsync(_stateFilePath, json);
            _cachedContainer = container; // Update cache
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving state container");
        }
    }

    public async Task<SeriesState?> GetExistingSeriesStateAsync(string seriesTitle)
    {
        var allStates = await LoadAllStatesAsync();

        var existingState = allStates?.FirstOrDefault(s => s != null &&
            (s.SeriesTitle != null && s.SeriesTitle.Equals(seriesTitle, StringComparison.OrdinalIgnoreCase)));

        return existingState;
    }

    private DiscPattern CreateDiscFingerprint(string discName, List<AkTitle> rippedTracks)
    {
        // Clean the disc name to get the base title
        var baseTitle = ExtractBaseNameForMatching(discName);

        return new DiscPattern
        {
            DiscTitle = baseTitle,
            TrackCount = rippedTracks.Count,
            SequenceNumber = 1, // Will be updated when checking existing patterns
            AssignedSeason = 0, // Will be set when processing
            StartingEpisode = 0, // Will be set when processing
            EpisodeCount = 0, // Will be set after processing
            ProcessedDate = DateTime.UtcNow
        };
    }
}
