using System;
using System.Collections.Generic;

namespace AutoMk.Models;

/// <summary>
/// Optimized model for user confirmation operations.
/// Contains only the properties needed for user interaction and confirmation dialogs.
/// </summary>
public class ConfirmationInfo
{
    /// <summary>
    /// The media title
    /// </summary>
    public string? Title { get; set; }
    
    /// <summary>
    /// The release year
    /// </summary>
    public string? Year { get; set; }
    
    /// <summary>
    /// The media type (movie, series, episode)
    /// </summary>
    public string? Type { get; set; }
    
    /// <summary>
    /// The IMDb ID (for reference)
    /// </summary>
    public string? ImdbID { get; set; }
    
    /// <summary>
    /// Plot/description for user confirmation
    /// </summary>
    public string? Plot { get; set; }
    
    /// <summary>
    /// OMDB API response status
    /// </summary>
    public string? Response { get; set; }
    
    
    /// <summary>
    /// Creates ConfirmationInfo from MediaDetails
    /// </summary>
    public static ConfirmationInfo FromMediaDetails(MediaDetails details)
    {
        return new ConfirmationInfo
        {
            Title = details.Identity.Title,
            Year = details.Identity.Year,
            Type = details.Identity.Type,
            ImdbID = details.Identity.ImdbID,
            Plot = details.Plot
        };
    }
    
    /// <summary>
    /// Creates ConfirmationInfo from EpisodeDetails
    /// </summary>
    public static ConfirmationInfo FromEpisodeDetails(EpisodeDetails episode)
    {
        return new ConfirmationInfo
        {
            Title = episode.Identity.Title,
            Year = episode.Identity.Year,
            Type = episode.Identity.Type,
            ImdbID = episode.Identity.ImdbID,
            Plot = episode.Plot
        };
    }
    
    /// <summary>
    /// Creates ConfirmationInfo from OptimizedSearchResult (plot will be null)
    /// </summary>
    public static ConfirmationInfo FromOptimizedSearchResult(OptimizedSearchResult searchResult, string? plot = null)
    {
        return new ConfirmationInfo
        {
            Title = searchResult.Title,
            Year = searchResult.Year,
            Type = searchResult.Type,
            ImdbID = searchResult.ImdbID,
            Plot = plot
        };
    }
    
    /// <summary>
    /// Creates ConfirmationInfo from MediaIdentity (plot will be null)
    /// </summary>
    public static ConfirmationInfo FromMediaIdentity(MediaIdentity identity, string? plot = null)
    {
        return new ConfirmationInfo
        {
            Title = identity.Title,
            Year = identity.Year,
            Type = identity.Type,
            ImdbID = identity.ImdbID,
            Plot = plot
        };
    }
    
    /// <summary>
    /// Converts this ConfirmationInfo to a MediaIdentity
    /// </summary>
    public MediaIdentity ToMediaIdentity()
    {
        return new MediaIdentity
        {
            Title = Title,
            Year = Year,
            Type = Type,
            ImdbID = ImdbID,
            Response = Response ?? "True"
        };
    }
    
    /// <summary>
    /// Gets a display string suitable for user confirmation dialogs
    /// </summary>
    public string GetDisplayString()
    {
        var parts = new List<string>();
        
        if (!string.IsNullOrWhiteSpace(Title))
            parts.Add(Title);
            
        if (!string.IsNullOrWhiteSpace(Year))
            parts.Add($"({Year})");
            
        if (!string.IsNullOrWhiteSpace(Type))
            parts.Add($"[{Type}]");
        
        return string.Join(" ", parts);
    }
    
    /// <summary>
    /// Gets a description suitable for confirmation dialogs (title + plot summary)
    /// </summary>
    public string GetDescriptionForConfirmation()
    {
        var description = GetDisplayString();
        
        if (!string.IsNullOrWhiteSpace(Plot))
        {
            // Truncate plot if too long for display
            var plotSummary = Plot.Length > 200 ? Plot.Substring(0, 197) + "..." : Plot;
            description += $"\n{plotSummary}";
        }
        
        return description;
    }
    
    /// <summary>
    /// Validates that this confirmation info has the minimum required data
    /// </summary>
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(Title) &&
               !string.IsNullOrWhiteSpace(Type);
    }
}