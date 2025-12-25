using System.Text.RegularExpressions;

namespace AutoMk.Utilities;

/// <summary>
/// Utility class for disc name parsing and cleaning operations
/// </summary>
public static class DiscNameUtility
{
    /// <summary>
    /// Regex patterns for parsing disc names in order of specificity.
    /// Matches patterns like "Frasier_S8_D1_BD", "Series_Name_S8_D1", etc.
    /// </summary>
    public static readonly string[] DiscNamePatterns =
    [
        @"^(.+?)_[Ss](\d+)_[Dd](\d+)",          // Frasier_S8_D1_BD
        @"^(.+?)[_\s][Ss](\d+)[_\s][Dd](\d+)",  // Frasier S8 D1 or Frasier_S8_D1
        @"(.+?)\s*[Ss](\d+)[^0-9]*[Dd](\d+)",   // Frasier S8 D1 (flexible spacing)
        @"(.+?)\s*Season\s*(\d+).*Disc\s*(\d+)" // Frasier Season 8 Disc 1
    ];

    /// <summary>
    /// Fallback pattern for extracting series name when main patterns don't match
    /// </summary>
    public const string FallbackSeriesNamePattern = @"^(.+?)(?:[_\s][Ss]?\d+|[_\s]Season)";

    /// <summary>
    /// Cleans a disc name by replacing underscores/hyphens with spaces and normalizing whitespace.
    /// Preserves disc/season identifiers for parsing.
    /// </summary>
    /// <param name="discName">The raw disc name to clean</param>
    /// <returns>Cleaned disc name with normalized spacing</returns>
    public static string CleanDiscName(string discName)
    {
        if (string.IsNullOrWhiteSpace(discName))
            return string.Empty;

        var cleaned = discName
            .Replace("_", " ")
            .Replace("-", " ")
            .Trim();

        // Remove multiple spaces
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

        return cleaned;
    }

    /// <summary>
    /// Extracts a clean series name suitable for OMDB searching by removing
    /// all disc, season, and format identifiers.
    /// </summary>
    /// <param name="discName">The raw disc name</param>
    /// <returns>Clean series name for search queries</returns>
    public static string ExtractSeriesNameForSearch(string discName)
    {
        if (string.IsNullOrWhiteSpace(discName))
            return string.Empty;

        var cleaned = discName
            .Replace("_", " ")
            .Replace("-", " ")
            .Trim();

        // Remove various disc/format identifiers
        cleaned = Regex.Replace(cleaned, @"\b(disc|disk|cd|dvd|bd|bluray|blu-ray)\b", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\b(season|s)\s*\d+\b", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\b[ds]\d+\b", "", RegexOptions.IgnoreCase); // Remove D1, D2, S8, etc.
        cleaned = Regex.Replace(cleaned, @"\b(part|pt)\s*\d+\b", "", RegexOptions.IgnoreCase);

        // Remove multiple spaces and trim
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

        return cleaned;
    }

    /// <summary>
    /// Attempts to parse structured disc information from a disc name.
    /// </summary>
    /// <param name="discName">The disc name to parse</param>
    /// <param name="seriesName">Extracted series name, or null if not found</param>
    /// <param name="season">Extracted season number, or null if not found</param>
    /// <param name="discNumber">Extracted disc number, or null if not found</param>
    /// <returns>True if a pattern matched successfully</returns>
    public static bool TryParseDiscName(string discName, out string? seriesName, out int? season, out int? discNumber)
    {
        seriesName = null;
        season = null;
        discNumber = null;

        if (string.IsNullOrWhiteSpace(discName))
            return false;

        foreach (var pattern in DiscNamePatterns)
        {
            var match = Regex.Match(discName, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                seriesName = match.Groups[1].Value.Replace("_", " ").Trim();
                season = int.Parse(match.Groups[2].Value);
                discNumber = int.Parse(match.Groups[3].Value);
                return true;
            }
        }

        // Try fallback pattern for series name only
        var fallbackMatch = Regex.Match(discName, FallbackSeriesNamePattern, RegexOptions.IgnoreCase);
        if (fallbackMatch.Success)
        {
            seriesName = fallbackMatch.Groups[1].Value.Replace("_", " ").Trim();
        }
        else
        {
            seriesName = ExtractSeriesNameForSearch(discName);
        }

        return false;
    }
}
