using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AutoMk.Interfaces;

namespace AutoMk.Services;

public class MediaNamingService : IMediaNamingService
{
    public string GenerateMovieFileName(string title, string? year, string extension)
    {
        var cleanTitle = CleanFileName(title);
        if (!string.IsNullOrEmpty(year))
        {
            return $"{cleanTitle} ({year}){extension}";
        }
        return $"{cleanTitle}{extension}";
    }

    public string GenerateEpisodeFileName(string seriesTitle, int season, int episode, string? episodeTitle, string extension)
    {
        var cleanSeriesTitle = CleanFileName(seriesTitle);
        var seasonEpisode = $"S{season:D2}E{episode:D2}";

        if (!string.IsNullOrEmpty(episodeTitle))
        {
            var cleanEpisodeTitle = CleanFileName(episodeTitle);
            return $"{cleanSeriesTitle} - {seasonEpisode} - {cleanEpisodeTitle}{extension}";
        }

        return $"{cleanSeriesTitle} - {seasonEpisode}{extension}";
    }

    public string GetMovieDirectory(string basePath, string title, string? year)
    {
        var cleanTitle = CleanFileName(title);
        if (!string.IsNullOrEmpty(year))
        {
            return Path.Combine(basePath, "Movies", $"{cleanTitle} ({year})");
        }
        return Path.Combine(basePath, "Movies", cleanTitle);
    }

    public string GetSeriesDirectory(string basePath, string seriesTitle)
    {
        var cleanTitle = CleanFileName(seriesTitle);
        return Path.Combine(basePath, "TV Shows", cleanTitle);
    }

    public string GetSeasonDirectory(int season)
    {
        return $"Season {season:D2}";
    }

    private string CleanFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = string.Join("", fileName.Select(c => invalid.Contains(c) ? ' ' : c));
        return Regex.Replace(cleaned, @"\s+", " ").Trim();
    }
}

