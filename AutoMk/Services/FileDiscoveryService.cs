using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutoMk.Interfaces;
using AutoMk.Models;
using AutoMk.Utilities;
using Microsoft.Extensions.Logging;

namespace AutoMk.Services;

/// <summary>
/// Service for discovering and locating ripped media files
/// </summary>
public class FileDiscoveryService : IFileDiscoveryService
{
    private readonly ILogger<FileDiscoveryService> _logger;

    public FileDiscoveryService(ILogger<FileDiscoveryService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Finds the ripped file for a given track in the output directory
    /// </summary>
    /// <param name="outputPath">The output directory path</param>
    /// <param name="track">The track information from MakeMKV</param>
    /// <param name="discName">Optional disc name for additional context</param>
    /// <returns>The full path to the ripped file or null if not found</returns>
    public string? FindRippedFile(string outputPath, AkTitle track, string discName = "")
    {
        if (string.IsNullOrEmpty(outputPath) || track == null)
        {
            _logger.LogWarning("Invalid parameters for file discovery");
            return null;
        }

        // First, try to find the file using the actual track name from MakeMKV
        if (!string.IsNullOrEmpty(track.Name))
        {
            // track.Name should contain the actual filename like "Frasier_S8_D1_BD_t00.mkv"
            var exactMatch = Path.Combine(outputPath, track.Name.Trim('"', ' '));
            if (File.Exists(exactMatch))
            {
                _logger.LogDebug($"Found file using exact track name: {track.Name}");
                return exactMatch;
            }
        }

        // Try alternative naming patterns based on track index
        var alternativeNames = GenerateAlternativeFileNames(track, discName);
        foreach (var altName in alternativeNames)
        {
            var altPath = Path.Combine(outputPath, altName);
            if (File.Exists(altPath))
            {
                _logger.LogDebug($"Found file using alternative name: {altName}");
                return altPath;
            }
        }

        // Try pattern matching for common MakeMKV output patterns
        var patternMatch = FindFileByPattern(outputPath, track, discName);
        if (patternMatch != null)
        {
            _logger.LogDebug($"Found file using pattern matching: {patternMatch}");
            return patternMatch;
        }

        // Last resort: search for any MKV files in the directory that might match
        var fallbackMatch = FindFileByFallback(outputPath, track);
        if (fallbackMatch != null)
        {
            _logger.LogDebug($"Found file using fallback search: {fallbackMatch}");
            return fallbackMatch;
        }

        _logger.LogWarning($"Could not find ripped file for track: {track.Name} in directory: {outputPath}");
        return null;
    }

    /// <summary>
    /// Finds all ripped files for a collection of tracks
    /// </summary>
    /// <param name="outputPath">The output directory path</param>
    /// <param name="tracks">The collection of tracks to find files for</param>
    /// <param name="discName">Optional disc name for additional context</param>
    /// <returns>Dictionary mapping track names to their file paths</returns>
    public Dictionary<string, string> FindRippedFiles(string outputPath, IEnumerable<AkTitle> tracks, string discName = "")
    {
        var results = new Dictionary<string, string>();
        
        if (string.IsNullOrEmpty(outputPath) || tracks == null)
        {
            _logger.LogWarning("Invalid parameters for bulk file discovery");
            return results;
        }

        foreach (var track in tracks)
        {
            var filePath = FindRippedFile(outputPath, track, discName);
            if (filePath != null)
            {
                results[track.Name ?? $"Track_{track.Id}"] = filePath;
            }
        }

        _logger.LogInformation($"Found {results.Count} out of {tracks.Count()} files in {outputPath}");
        return results;
    }

    /// <summary>
    /// Verifies that a file exists and is accessible
    /// </summary>
    /// <param name="filePath">The file path to verify</param>
    /// <returns>True if the file exists and is accessible, false otherwise</returns>
    public bool VerifyFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return false;
        }

        try
        {
            var fileInfo = new FileInfo(filePath);
            return fileInfo.Exists && fileInfo.Length > 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error verifying file {filePath}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Gets the file size in bytes for a given file path
    /// </summary>
    /// <param name="filePath">The file path to check</param>
    /// <returns>The file size in bytes or -1 if the file doesn't exist</returns>
    public long GetFileSize(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return -1;
        }

        try
        {
            var fileInfo = new FileInfo(filePath);
            return fileInfo.Exists ? fileInfo.Length : -1;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error getting file size for {filePath}: {ex.Message}");
            return -1;
        }
    }

    /// <summary>
    /// Cleans up temporary or incomplete files in the output directory
    /// </summary>
    /// <param name="outputPath">The output directory path</param>
    /// <param name="excludeFiles">Files to exclude from cleanup</param>
    public void CleanupTempFiles(string outputPath, IEnumerable<string>? excludeFiles = null)
    {
        if (!FileSystemHelper.IsValidDirectoryPath(outputPath))
        {
            return;
        }

        var excludeSet = excludeFiles?.ToHashSet() ?? new HashSet<string>();
        var tempPatterns = new[] { "*.tmp", "*.part", "*.temp", "*.incomplete" };

        try
        {
            foreach (var pattern in tempPatterns)
            {
                var tempFiles = Directory.GetFiles(outputPath, pattern);
                foreach (var tempFile in tempFiles)
                {
                    var fileName = Path.GetFileName(tempFile);
                    if (!excludeSet.Contains(fileName) && !excludeSet.Contains(tempFile))
                    {
                        try
                        {
                            File.Delete(tempFile);
                            _logger.LogDebug($"Deleted temporary file: {tempFile}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Could not delete temporary file {tempFile}: {ex.Message}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error during cleanup of {outputPath}: {ex.Message}");
        }
    }

    private List<string> GenerateAlternativeFileNames(AkTitle track, string discName)
    {
        var alternatives = new List<string>();
        
        // Generate alternatives based on track index
        var trackIndex = track.Id;
        var extensions = new[] { ".mkv", ".mp4", ".avi" };
        
        foreach (var ext in extensions)
        {
            alternatives.Add($"title_{trackIndex}{ext}");
            alternatives.Add($"title{trackIndex}{ext}");
            alternatives.Add($"t{trackIndex}{ext}");
            
            if (!string.IsNullOrEmpty(discName))
            {
                alternatives.Add($"{discName}_t{trackIndex}{ext}");
                alternatives.Add($"{discName}_{trackIndex}{ext}");
            }
        }
        
        return alternatives;
    }

    private string? FindFileByPattern(string outputPath, AkTitle track, string discName)
    {
        if (!FileSystemHelper.IsValidDirectoryPath(outputPath))
        {
            return null;
        }

        var patterns = new List<string>();
        var trackIndex = track.Id;
        
        // Common MakeMKV patterns
        patterns.Add($"*t{trackIndex}*.mkv");
        patterns.Add($"*title{trackIndex}*.mkv");
        patterns.Add($"*_{trackIndex}_*.mkv");
        
        if (!string.IsNullOrEmpty(discName))
        {
            patterns.Add($"{discName}*t{trackIndex}*.mkv");
            patterns.Add($"*{discName}*{trackIndex}*.mkv");
        }

        foreach (var pattern in patterns)
        {
            try
            {
                var matches = Directory.GetFiles(outputPath, pattern);
                if (matches.Length > 0)
                {
                    return matches[0]; // Return first match
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error searching with pattern {pattern}: {ex.Message}");
            }
        }

        return null;
    }

    private string? FindFileByFallback(string outputPath, AkTitle track)
    {
        if (!FileSystemHelper.IsValidDirectoryPath(outputPath))
        {
            return null;
        }

        try
        {
            var allMkvFiles = Directory.GetFiles(outputPath, "*.mkv");
            
            // If only one MKV file exists, assume it's the one we want
            if (allMkvFiles.Length == 1)
            {
                return allMkvFiles[0];
            }

            // Try to find files created around the same time as the track processing
            var recentFiles = allMkvFiles
                .Where(f => File.GetCreationTime(f) > DateTime.Now.AddHours(-1))
                .OrderBy(f => File.GetCreationTime(f))
                .ToArray();

            if (recentFiles.Length > 0)
            {
                return recentFiles[0];
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error in fallback file search: {ex.Message}");
        }

        return null;
    }
}