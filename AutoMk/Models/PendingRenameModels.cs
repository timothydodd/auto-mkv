using System;
using System.Collections.Generic;

namespace AutoMk.Models;

/// <summary>
/// Represents a pending file rename operation that hasn't been executed yet.
/// Used to collect proposed renames before showing confirmation to the user.
/// </summary>
public class PendingRename
{
    /// <summary>
    /// Full path to the original file
    /// </summary>
    public string OriginalFilePath { get; set; } = string.Empty;

    /// <summary>
    /// Just the filename (for display)
    /// </summary>
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>
    /// Full path where the file will be moved to
    /// </summary>
    public string ProposedFilePath { get; set; } = string.Empty;

    /// <summary>
    /// Just the new filename (for display)
    /// </summary>
    public string ProposedFileName { get; set; } = string.Empty;

    /// <summary>
    /// The track this rename is for
    /// </summary>
    public AkTitle? Track { get; set; }

    /// <summary>
    /// File size in GB (for display)
    /// </summary>
    public double SizeInGB { get; set; }

    // TV Series specific properties
    public int? Season { get; set; }
    public int? EpisodeNumber { get; set; }
    public string? EpisodeTitle { get; set; }
    public SeriesInfo? SeriesInfo { get; set; }

    // Movie specific properties
    public MovieInfo? MovieInfo { get; set; }

    /// <summary>
    /// Index of this track in the processing order
    /// </summary>
    public int TrackIndex { get; set; }

    /// <summary>
    /// If true, user wants to re-lookup this track
    /// </summary>
    public bool RequiresRelookup { get; set; }

    /// <summary>
    /// Whether this is a TV series episode (vs movie)
    /// </summary>
    public bool IsTvSeries => SeriesInfo != null;

    /// <summary>
    /// Whether this is a movie
    /// </summary>
    public bool IsMovie => MovieInfo != null;

    /// <summary>
    /// Gets a display string for the media type
    /// </summary>
    public string MediaTypeDisplay => IsTvSeries
        ? $"S{Season:D2}E{EpisodeNumber:D2}"
        : MovieInfo?.Year?.ToString() ?? "";
}

/// <summary>
/// Result from the batch rename confirmation workflow
/// </summary>
public class BatchRenameResult
{
    /// <summary>
    /// List of pending renames (may have been modified by user re-lookups)
    /// </summary>
    public List<PendingRename> PendingRenames { get; set; } = new();

    /// <summary>
    /// Tracks that were skipped by the user
    /// </summary>
    public List<PendingRename> SkippedTracks { get; set; } = new();

    /// <summary>
    /// True if user confirmed to proceed with renaming
    /// </summary>
    public bool UserConfirmed { get; set; }

    /// <summary>
    /// True if user cancelled the entire operation
    /// </summary>
    public bool UserCancelled { get; set; }

    /// <summary>
    /// Creates a successful result with the given renames
    /// </summary>
    public static BatchRenameResult Success(List<PendingRename> renames) => new()
    {
        PendingRenames = renames,
        UserConfirmed = true,
        UserCancelled = false
    };

    /// <summary>
    /// Creates a cancelled result
    /// </summary>
    public static BatchRenameResult Cancelled() => new()
    {
        UserConfirmed = false,
        UserCancelled = true
    };
}

/// <summary>
/// Options for configuring the batch rename confirmation behavior
/// </summary>
public class BatchRenameOptions
{
    /// <summary>
    /// Skip confirmation if there's only one track
    /// </summary>
    public bool SkipConfirmationForSingleTrack { get; set; } = true;

    /// <summary>
    /// Allow user to re-lookup tracks
    /// </summary>
    public bool AllowRelookup { get; set; } = true;

    /// <summary>
    /// Allow user to skip individual tracks
    /// </summary>
    public bool AllowSkipTracks { get; set; } = true;
}
