using System.Collections.Generic;
using AutoMk.Models;

namespace AutoMk.Interfaces;

/// <summary>
/// Service for discovering and locating ripped media files
/// </summary>
public interface IFileDiscoveryService
{
    /// <summary>
    /// Finds the ripped file for a given track in the output directory
    /// </summary>
    /// <param name="outputPath">The output directory path</param>
    /// <param name="track">The track information from MakeMKV</param>
    /// <param name="discName">Optional disc name for additional context</param>
    /// <returns>The full path to the ripped file or null if not found</returns>
    string? FindRippedFile(string outputPath, AkTitle track, string discName = "");

    /// <summary>
    /// Finds all ripped files for a collection of tracks
    /// </summary>
    /// <param name="outputPath">The output directory path</param>
    /// <param name="tracks">The collection of tracks to find files for</param>
    /// <param name="discName">Optional disc name for additional context</param>
    /// <returns>Dictionary mapping track names to their file paths</returns>
    Dictionary<string, string> FindRippedFiles(string outputPath, IEnumerable<AkTitle> tracks, string discName = "");

    /// <summary>
    /// Verifies that a file exists and is accessible
    /// </summary>
    /// <param name="filePath">The file path to verify</param>
    /// <returns>True if the file exists and is accessible, false otherwise</returns>
    bool VerifyFile(string filePath);

    /// <summary>
    /// Gets the file size in bytes for a given file path
    /// </summary>
    /// <param name="filePath">The file path to check</param>
    /// <returns>The file size in bytes or -1 if the file doesn't exist</returns>
    long GetFileSize(string filePath);

    /// <summary>
    /// Cleans up temporary or incomplete files in the output directory
    /// </summary>
    /// <param name="outputPath">The output directory path</param>
    /// <param name="excludeFiles">Files to exclude from cleanup</param>
    void CleanupTempFiles(string outputPath, IEnumerable<string>? excludeFiles = null);
}