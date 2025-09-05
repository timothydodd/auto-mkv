
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using AutoMk.Interfaces;

namespace AutoMk.Models;

public enum TrackSortingStrategy
{
    ByTrackOrder = 0,  // Sort by MakeMKV track order (default)
    ByMplsFileName = 1, // Sort by MPLS file name (e.g., 00042.mpls, 00043.mpls)
    UserConfirmed = 2  // Sort by track order but confirm each episode with user
}

public enum DoubleEpisodeHandling
{
    AlwaysAsk = 0,      // Always prompt user for each long episode (default)
    AlwaysSingle = 1,   // Always treat as single episode regardless of length
    AlwaysDouble = 2    // Always treat oversized episodes as double
}

public class SeriesState
{
    public string SeriesTitle { get; set; } = string.Empty;
    public int CurrentSeason { get; set; }
    public int NextEpisode { get; set; }
    public int NextDiscNumber { get; set; } = 1;
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<DiscInfo> ProcessedDiscs { get; set; } = new();
    
    public bool AutoIncrement { get; set; } = false;
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AutoIncrementPreference { get; set; } = null; // null = not set, true = user chose yes, false = user chose no
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Dictionary<int, int> SeasonEpisodeCounts { get; set; } = new();
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<DiscPattern> KnownDiscPatterns { get; set; } = new();
    
    // Configuration properties - these should come from SeriesProfile instead
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Obsolete("Use SeriesProfile.MinEpisodeSizeGB instead")]
    public double? MinEpisodeSizeGB { get; set; } = null;
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Obsolete("Use SeriesProfile.MaxEpisodeSizeGB instead")]
    public double? MaxEpisodeSizeGB { get; set; } = null;
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Obsolete("Use SeriesProfile.TrackSortingStrategy instead")]
    public TrackSortingStrategy? TrackSortingStrategy { get; set; } = null;
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Obsolete("Use SeriesProfile.DoubleEpisodeHandling instead")]
    public DoubleEpisodeHandling? DoubleEpisodeHandling { get; set; } = null;
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<EpisodeTrackPattern> LearnedPatterns { get; set; } = new(); // Learned episode-to-track mappings
}

public class DiscInfo
{
    public string DiscName { get; set; } = string.Empty;
    public int Season { get; set; }
    public int DiscNumber { get; set; } = 1;
    public int StartingEpisode { get; set; }
    public int TrackCount { get; set; } // Number of physical tracks/files
    public int EpisodeCount { get; set; } // Number of episodes (may differ due to double-length)
    public DateTime ProcessedDate { get; set; }
    
    // Maps track index to episode numbers (for handling double-length episodes)
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Dictionary<int, List<int>> TrackToEpisodeMapping { get; set; } = new();
    
    // Track user's manual episode selections for pattern learning
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<TrackSelectionPattern> UserSelections { get; set; } = new();
}

public class ParsedDiscInfo
{
    public string SeriesName { get; set; } = string.Empty;
    public int Season { get; set; } = 1;
    public int DiscNumber { get; set; } = 1;
}

public class CachedMediaInfo
{
    public string? Title { get; set; }
    public string? ImdbID { get; set; }
    public string? Type { get; set; }
    public string? Year { get; set; }
    public string Response { get; set; } = "True";
    
    // Convert to MediaIdentity
    public MediaIdentity ToMediaIdentity()
    {
        return new MediaIdentity
        {
            Title = Title,
            ImdbID = ImdbID,
            Type = Type,
            Year = Year,
            Response = Response
        };
    }
}

public class ManualIdentification
{
    public string DiscNamePattern { get; set; } = string.Empty;
    public string MediaTitle { get; set; } = string.Empty;
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ImdbId { get; set; }
    
    public string MediaType { get; set; } = string.Empty; // "movie" or "series"
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Year { get; set; }
    
    public DateTime IdentifiedDate { get; set; }
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CachedMediaInfo? CachedOmdbData { get; set; }
}

public class DiscPattern
{
    public string DiscTitle { get; set; } = string.Empty;
    public int TrackCount { get; set; }
    public int SequenceNumber { get; set; } // Which occurrence of this pattern (1st, 2nd, 3rd, etc.)
    public int AssignedSeason { get; set; }
    public int StartingEpisode { get; set; }
    public int EpisodeCount { get; set; }
    public DateTime ProcessedDate { get; set; }
}

public class MediaStateContainer
{
    public List<SeriesState> SeriesStates { get; set; } = new();
    public List<ManualIdentification> ManualIdentifications { get; set; } = new();
}

public class PreIdentifiedMedia
{
    public MediaIdentity? MediaData { get; set; }
    public bool IsFromCache { get; set; }
    public string IdentificationSource { get; set; } = string.Empty; // "manual", "automatic", "interactive"
}

public class SeriesProfile : ISizeConfigurable
{
    public string SeriesTitle { get; set; } = string.Empty;
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? MinEpisodeSizeGB { get; set; }
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? MaxEpisodeSizeGB { get; set; }
    
    public TrackSortingStrategy TrackSortingStrategy { get; set; } = TrackSortingStrategy.ByTrackOrder;
    public DoubleEpisodeHandling DoubleEpisodeHandling { get; set; } = DoubleEpisodeHandling.AlwaysAsk;
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DefaultStartingSeason { get; set; }
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DefaultStartingEpisode { get; set; }
    
    public bool UseAutoIncrement { get; set; } = false;
    public bool AlwaysSkipConfirmation { get; set; } = false;
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public DateTime LastModifiedDate { get; set; } = DateTime.Now;
    
    // ISizeConfigurable implementation - maps to episode-specific properties
    [JsonIgnore]
    public double MinSizeGB 
    { 
        get => MinEpisodeSizeGB ?? 0.0; 
        set => MinEpisodeSizeGB = value; 
    }
    
    [JsonIgnore]
    public double MaxSizeGB 
    { 
        get => MaxEpisodeSizeGB ?? 50.0; 
        set => MaxEpisodeSizeGB = value; 
    }
}

public class RipConfirmation : ISizeConfigurable
{
    public string MediaTitle { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public int TracksToRip { get; set; }
    public double MinSizeGB { get; set; }
    public double MaxSizeGB { get; set; }
    public string SortingMethod { get; set; } = string.Empty;
    public string StartingPosition { get; set; } = string.Empty;
    public string DoubleEpisodeHandling { get; set; } = string.Empty;
    public List<AkTitle> SelectedTracks { get; set; } = new();
}

public enum MediaTypePrediction
{
    Unknown = 0,
    Movie = 1,
    TvSeries = 2
}

public enum RipConfirmationResult
{
    Proceed = 0,        // User wants to proceed with ripping
    ModifySettings = 1, // User wants to modify settings
    Skip = 2,           // User wants to skip this disc
    ProceedAndDontAskAgain = 3,  // User wants to proceed and not be asked again for this series
    Retry = 4           // User wants to retry the operation
}

public class SeasonInfoCache
{
    public string SeriesTitle { get; set; } = string.Empty;
    public int Season { get; set; }
    public List<CachedEpisodeInfo> Episodes { get; set; } = new();
    public DateTime CachedDate { get; set; }
    public DateTime ExpiryDate { get; set; }
}

public class CachedEpisodeInfo
{
    public int EpisodeNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ImdbId { get; set; } = string.Empty;
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? Released { get; set; }
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Plot { get; set; }
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ImdbRating { get; set; }
}

/// <summary>
/// Tracks a user's manual episode selection for pattern learning
/// </summary>
public class TrackSelectionPattern
{
    public string TrackId { get; set; } = string.Empty;
    public string TrackName { get; set; } = string.Empty;
    public int TrackOrderPosition { get; set; } // Position in the track order (0-based)
    public int SuggestedEpisode { get; set; } // What the system suggested
    public int SelectedEpisode { get; set; } // What the user actually selected
    public bool WasAccepted { get; set; } // true if user accepted suggestion, false if they chose different
    public DateTime SelectionDate { get; set; }
    public string SelectionReason { get; set; } = string.Empty; // "accepted" or "manual_choice"
}

/// <summary>
/// Learned patterns for episode-to-track mapping based on user selections
/// </summary>
public class EpisodeTrackPattern
{
    public string DiscNamePattern { get; set; } = string.Empty; // Pattern like "SeriesName_S*_D*"
    public int Season { get; set; }
    public int DiscNumber { get; set; }
    public List<TrackToEpisodeMapping> TrackMappings { get; set; } = new();
    public int UsageCount { get; set; } // How many times this pattern has been confirmed
    public double ConfidenceScore { get; set; } // 0.0 to 1.0 based on user acceptance rate
    public DateTime LastUsed { get; set; }
    public DateTime CreatedDate { get; set; }
}

/// <summary>
/// Maps a specific track position to episode number
/// </summary>
public class TrackToEpisodeMapping
{
    public int TrackPosition { get; set; } // 0-based position in track list
    public int EpisodeNumber { get; set; }
    public double ConfidenceScore { get; set; } // How confident we are in this mapping
}

public class SeasonCacheContainer
{
    public List<SeasonInfoCache> CachedSeasons { get; set; } = new();
}
