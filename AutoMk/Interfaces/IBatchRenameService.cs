using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMk.Models;
using AutoMk.Services;

namespace AutoMk.Interfaces;

/// <summary>
/// Service for collecting, confirming, and executing batch file renames.
/// Provides a workflow where users can review all proposed renames before execution.
/// </summary>
public interface IBatchRenameService
{
    /// <summary>
    /// Collects proposed renames for TV series episodes without executing them.
    /// </summary>
    /// <param name="outputPath">The base output directory containing ripped files</param>
    /// <param name="discName">Name of the disc being processed</param>
    /// <param name="tracks">List of tracks to process</param>
    /// <param name="seriesInfo">Information about the TV series</param>
    /// <param name="discInfo">Information about the disc including season and episode mapping</param>
    /// <param name="seriesState">Current state of the series processing</param>
    /// <returns>List of pending renames</returns>
    Task<List<PendingRename>> CollectTvSeriesRenamesAsync(
        string outputPath,
        string discName,
        List<AkTitle> tracks,
        SeriesInfo seriesInfo,
        DiscInfo discInfo,
        SeriesState seriesState);

    /// <summary>
    /// Collects proposed renames for movies without executing them.
    /// Used for multi-movie disc processing.
    /// </summary>
    /// <param name="outputPath">The base output directory containing ripped files</param>
    /// <param name="discName">Name of the disc being processed</param>
    /// <param name="trackMovieMapping">Dictionary mapping each track to its movie info</param>
    /// <returns>List of pending renames</returns>
    Task<List<PendingRename>> CollectMovieRenamesAsync(
        string outputPath,
        string discName,
        Dictionary<AkTitle, MovieInfo> trackMovieMapping);

    /// <summary>
    /// Collects a proposed rename for a single movie track.
    /// </summary>
    /// <param name="outputPath">The base output directory</param>
    /// <param name="track">The track to rename</param>
    /// <param name="movieInfo">Information about the movie</param>
    /// <param name="discName">Name of the disc (optional)</param>
    /// <returns>A pending rename, or null if file not found</returns>
    Task<PendingRename?> CollectSingleMovieRenameAsync(
        string outputPath,
        AkTitle track,
        MovieInfo movieInfo,
        string? discName = null);

    /// <summary>
    /// Shows confirmation UI for pending renames and allows user to re-lookup tracks.
    /// </summary>
    /// <param name="pendingRenames">List of proposed renames to confirm</param>
    /// <param name="options">Optional configuration for confirmation behavior</param>
    /// <returns>Result containing confirmed renames or cancellation status</returns>
    Task<BatchRenameResult> ConfirmAndProcessRenamesAsync(
        List<PendingRename> pendingRenames,
        BatchRenameOptions? options = null);

    /// <summary>
    /// Executes the confirmed renames by moving files to their new locations.
    /// </summary>
    /// <param name="confirmedRenames">List of renames to execute</param>
    /// <returns>True if all renames succeeded, false if any failed</returns>
    Task<bool> ExecuteRenamesAsync(List<PendingRename> confirmedRenames);

    /// <summary>
    /// Allows user to re-lookup and update a specific pending rename.
    /// </summary>
    /// <param name="pendingRename">The rename to update</param>
    /// <returns>Updated pending rename, or the original if user cancels</returns>
    Task<PendingRename> RelookupTrackAsync(PendingRename pendingRename);

    /// <summary>
    /// Collects proposed renames for TV series episodes from manual episode mapping.
    /// Used in Manual Mode where user specifies episode numbers directly.
    /// </summary>
    /// <param name="outputPath">The base output directory containing ripped files</param>
    /// <param name="discName">Name of the disc being processed</param>
    /// <param name="seriesInfo">Information about the TV series</param>
    /// <param name="episodeMapping">Dictionary mapping each track to its episode info (season/episode)</param>
    /// <returns>List of pending renames</returns>
    Task<List<PendingRename>> CollectManualTvSeriesRenamesAsync(
        string outputPath,
        string discName,
        SeriesInfo seriesInfo,
        Dictionary<AkTitle, EpisodeInfo> episodeMapping);
}
