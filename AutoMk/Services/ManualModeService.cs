using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMk.Extensions;
using AutoMk.Interfaces;
using AutoMk.Models;
using AutoMk.Utilities;
using Microsoft.Extensions.Logging;

namespace AutoMk.Services;

public class ManualModeService
{
    private readonly IOmdbClient _omdbClient;
    private readonly ILogger<ManualModeService> _logger;
    private readonly IConsolePromptService _promptService;

    public ManualModeService(IOmdbClient omdbClient, ILogger<ManualModeService> logger, IConsolePromptService promptService)
    {
        _omdbClient = ValidationHelper.ValidateNotNull(omdbClient);
        _logger = ValidationHelper.ValidateNotNull(logger);
        _promptService = ValidationHelper.ValidateNotNull(promptService);
    }

    public async Task<MediaIdentity?> IdentifyMediaWithConfirmationAsync(string discName)
    {
        Console.WriteLine();
        _promptService.DisplayHeader($"MANUAL MODE - Media Identification for disc: {discName}");

        // First, try automatic identification
        var searchTitle = CleanDiscNameForSearch(discName);
        Console.WriteLine($"Attempting automatic identification for: '{searchTitle}'");
        Console.WriteLine();

        // Search for both movie and series
        var seriesTask = _omdbClient.GetSeries(searchTitle);
        var movieTask = _omdbClient.GetMovie(searchTitle, null);
        
        await Task.WhenAll(seriesTask, movieTask);
        
        var seriesResult = await seriesTask;
        var movieResult = await movieTask;
        
        // Check if we found results
        bool foundSeries = seriesResult.IsValidOmdbResponse();
        bool foundMovie = movieResult.IsValidOmdbResponse();

        if (foundSeries || foundMovie)
        {
            Console.WriteLine("Automatic identification found the following results:");
            Console.WriteLine();

            if (foundMovie)
            {
                Console.WriteLine($"MOVIE: {movieResult.Title} ({movieResult.Year})");
                if (!string.IsNullOrEmpty(movieResult.Plot))
                {
                    Console.WriteLine($"   Plot: {movieResult.Plot}");
                }
                Console.WriteLine();
            }

            if (foundSeries)
            {
                Console.WriteLine($"TV SERIES: {seriesResult.Title} ({seriesResult.Year})");
                if (!string.IsNullOrEmpty(seriesResult.Plot))
                {
                    Console.WriteLine($"   Plot: {seriesResult.Plot}");
                }
                Console.WriteLine();
            }

            // Let user choose
            if (foundSeries && foundMovie)
            {
                var movieInfo = ModelConverter.ToConfirmationInfo(movieResult);
                var seriesInfo = ModelConverter.ToConfirmationInfo(seriesResult);
                return await SelectBetweenFoundResults(movieInfo, seriesInfo);
            }
            else if (foundMovie)
            {
                var movieInfo = ModelConverter.ToConfirmationInfo(movieResult);
                return await ConfirmMovieSelection(movieInfo);
            }
            else
            {
                var seriesInfo = ModelConverter.ToConfirmationInfo(seriesResult);
                return await ConfirmSeriesSelection(seriesInfo);
            }
        }

        // No automatic results found - let user search manually
        Console.WriteLine("No automatic results found. Please search manually.");
        return await PerformManualSearchAsync();
    }

    public MediaType PromptForMediaType()
    {
        var result = _promptService.SelectPrompt<MediaType>(new SelectPromptOptions
        {
            HeaderText = "MANUAL MODE - Media Type Selection",
            Question = "What type of media is this disc?",
            Choices = new List<PromptChoice>
            {
                new("movie", "Movie", MediaType.Movie),
                new("series", "TV Series", MediaType.TvSeries)
            },
            AllowCancel = false,
            ClearScreenBefore = false
        });

        var selectedType = result.Success ? result.Value : MediaType.Movie;
        _logger.LogInformation($"User selected: {selectedType}");
        return selectedType;
    }

    public List<AkTitle> SelectTracksToRip(List<AkTitle> availableTitles)
    {
        Console.WriteLine();
        _promptService.DisplayHeader("MANUAL MODE - Track Selection");
        Console.WriteLine("Available tracks on disc:");
        Console.WriteLine();

        for (int i = 0; i < availableTitles.Count; i++)
        {
            var title = availableTitles[i];
            Console.WriteLine($"{i + 1}. Track {title.Id}: {title.Name}");
            Console.WriteLine($"   Duration: {title.Length}");
            Console.WriteLine($"   Size: {title.Size} ({title.SizeInGB:F2} GB)");
            Console.WriteLine();
        }

        var trackSelectionResult = _promptService.TextPrompt(new TextPromptOptions
        {
            Question = "Select tracks to rip (enter numbers separated by commas, or 'all' for all tracks):",
            Required = true,
            PromptText = "Selection",
            ValidationPattern = @"^(all|\d+(,\s*\d+)*)$",
            ValidationMessage = "Please enter track numbers separated by commas (e.g., 1,3,5) or 'all'"
        });
        
        if (!trackSelectionResult.Success || trackSelectionResult.Cancelled)
        {
            _logger.LogInformation("User cancelled track selection");
            return new List<AkTitle>();
        }
        
        var input = trackSelectionResult.Value.Trim();
        
        if (input.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("User selected all tracks");
            return availableTitles;
        }
        
        var selectedTracks = new List<AkTitle>();
        var parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var part in parts)
        {
            if (int.TryParse(part.Trim(), out int trackNumber) && 
                trackNumber >= 1 && trackNumber <= availableTitles.Count)
            {
                selectedTracks.Add(availableTitles[trackNumber - 1]);
            }
        }
        
        Console.WriteLine($"Selected {selectedTracks.Count} track(s):");
        foreach (var track in selectedTracks)
        {
            Console.WriteLine($"  - Track {track.Id}: {track.Name} ({track.SizeInGB:F2} GB)");
        }
        Console.WriteLine();
        
        _logger.LogInformation($"User selected {selectedTracks.Count} tracks");
        return selectedTracks;
    }

    public Dictionary<AkTitle, EpisodeInfo> MapTracksToEpisodes(List<AkTitle> selectedTracks, string seriesTitle)
    {
        Console.WriteLine();
        _promptService.DisplayHeader($"MANUAL MODE - Episode Mapping for {seriesTitle}");
        Console.WriteLine("Map each track to season and episode information:");
        Console.WriteLine();

        var trackMapping = new Dictionary<AkTitle, EpisodeInfo>();

        foreach (var track in selectedTracks)
        {
            Console.WriteLine($"Track {track.Id}: {track.Name} ({track.SizeInGB:F2} GB)");
            
            var seasonResult = _promptService.NumberPrompt(new NumberPromptOptions
            {
                Question = "Season number:",
                Required = true,
                PromptText = "Season",
                MinValue = 1,
                MaxValue = 50
            });
            
            var episodeResult = _promptService.NumberPrompt(new NumberPromptOptions
            {
                Question = "Episode number:",
                Required = true,
                PromptText = "Episode",
                MinValue = 1,
                MaxValue = 999
            });
            
            int season = seasonResult.Success ? seasonResult.Value : 1;
            int episode = episodeResult.Success ? episodeResult.Value : 1;
            
            trackMapping[track] = new EpisodeInfo
            {
                Season = season,
                Episode = episode
            };
            
            Console.WriteLine($"  Mapped to S{season:D2}E{episode:D2}");
            Console.WriteLine();
        }

        return trackMapping;
    }

    private async Task<MediaIdentity?> SelectBetweenFoundResults(ConfirmationInfo movieResult, ConfirmationInfo seriesResult)
    {
        var result = _promptService.SelectPrompt(new SelectPromptOptions
        {
            Question = "Which result is correct?",
            Choices = new List<PromptChoice>
            {
                new("movie", "Movie result"),
                new("series", "TV Series result"),
                new("search", "Neither - search manually"),
                new("skip", "Skip identification")
            }
        });

        if (!result.Success || result.Cancelled)
        {
            _logger.LogInformation("User cancelled result selection");
            return null;
        }

        return result.Value switch
        {
            "movie" => DoConfirmMovie(movieResult),
            "series" => DoConfirmSeries(seriesResult),
            "search" => await PerformManualSearchAsync(),
            "skip" => DoSkipIdentification(),
            _ => null
        };
    }

    private async Task<MediaIdentity?> ConfirmMovieSelection(ConfirmationInfo movieResult)
    {
        Console.WriteLine($"Found movie: {movieResult.Title} ({movieResult.Year})");
        Console.WriteLine();

        var result = _promptService.SelectPrompt(new SelectPromptOptions
        {
            Question = "Is this the correct movie?",
            Choices = new List<PromptChoice>
            {
                new("confirm", "Yes - use this movie"),
                new("search", "No - search manually"), 
                new("skip", "Skip identification")
            }
        });

        if (!result.Success || result.Cancelled)
        {
            _logger.LogInformation("User cancelled movie confirmation");
            return null;
        }

        return result.Value switch
        {
            "confirm" => DoConfirmMovie(movieResult),
            "search" => await PerformManualSearchAsync(),
            "skip" => DoSkipIdentification(),
            _ => null
        };
    }

    private MediaIdentity DoConfirmMovie(ConfirmationInfo movieResult)
    {
        _logger.LogInformation($"User confirmed movie: {movieResult.Title}");
        return ModelConverter.ToMediaIdentity(movieResult);
    }

    private MediaIdentity? DoSkipIdentification()
    {
        _logger.LogInformation("User skipped identification");
        return null;
    }

    private async Task<MediaIdentity?> ConfirmSeriesSelection(ConfirmationInfo seriesResult)
    {
        Console.WriteLine($"Found TV series: {seriesResult.Title} ({seriesResult.Year})");
        Console.WriteLine();

        var result = _promptService.SelectPrompt(new SelectPromptOptions
        {
            Question = "Is this the correct TV series?",
            Choices = new List<PromptChoice>
            {
                new("confirm", "Yes - use this series"),
                new("search", "No - search manually"),
                new("skip", "Skip identification")
            }
        });

        if (!result.Success || result.Cancelled)
        {
            _logger.LogInformation("User cancelled TV series confirmation");
            return null;
        }

        return result.Value switch
        {
            "confirm" => DoConfirmSeries(seriesResult),
            "search" => await PerformManualSearchAsync(),
            "skip" => DoSkipIdentification(),
            _ => null
        };
    }

    private MediaIdentity DoConfirmSeries(ConfirmationInfo seriesResult)
    {
        _logger.LogInformation($"User confirmed TV series: {seriesResult.Title}");
        return ModelConverter.ToMediaIdentity(seriesResult);
    }

    private async Task<MediaIdentity?> PerformManualSearchAsync()
    {
        _promptService.DisplayHeader("Manual Search");
        
        // Get media type
        var typeResult = _promptService.SelectPrompt<MediaType>(new SelectPromptOptions
        {
            Question = "What type of media are you searching for?",
            Choices = new List<PromptChoice>
            {
                new("movie", "Movie", MediaType.Movie),
                new("series", "TV Series", MediaType.TvSeries)
            }
        });

        if (!typeResult.Success || typeResult.Cancelled)
        {
            return null;
        }

        bool isMovie = typeResult.Value == MediaType.Movie;
        
        // Get title
        var titleResult = _promptService.TextPrompt(new TextPromptOptions
        {
            Question = $"Enter {(isMovie ? "movie" : "TV series")} title:",
            Required = true,
            PromptText = "Title"
        });

        if (!titleResult.Success || titleResult.Cancelled)
        {
            return null;
        }

        // Get year (optional)
        var yearResult = _promptService.NumberPrompt(new NumberPromptOptions
        {
            Question = "Enter year (optional):",
            Required = false,
            PromptText = "Year",
            MinValue = 1900,
            MaxValue = DateTime.Now.Year + 5
        });

        int? year = yearResult.Success ? yearResult.Value : null;
        
        Console.WriteLine($"Searching for {(isMovie ? "movie" : "TV series")}: '{titleResult.Value}'{(year.HasValue ? $" ({year})" : "")}");

        try
        {
            ConfirmationInfo? result;
            if (isMovie)
            {
                var movieResult = await _omdbClient.GetMovie(titleResult.Value, year);
                result = movieResult != null ? ModelConverter.ToConfirmationInfo(movieResult) : null;
            }
            else
            {
                var seriesResult = await _omdbClient.GetSeries(titleResult.Value);
                result = seriesResult != null ? ModelConverter.ToConfirmationInfo(seriesResult) : null;
            }

            if (result.IsValidOmdbResponse())
            {
                Console.WriteLine($"Found: {result.Title} ({result.Year})");
                if (!string.IsNullOrEmpty(result.Plot))
                {
                    Console.WriteLine($"Plot: {result.Plot}");
                }
                
                var confirmResult = _promptService.ConfirmPrompt(new ConfirmPromptOptions
                {
                    Question = "Is this correct?",
                    DefaultValue = false
                });
                
                if (confirmResult.Success && confirmResult.Value)
                {
                    _logger.LogInformation($"User confirmed manual search result: {result.Title}");
                    return ModelConverter.ToMediaIdentity(result);
                }
            }
            else
            {
                Console.WriteLine("No results found for that search.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during manual search");
            Console.WriteLine($"Error during search: {ex.Message}");
        }

        return null;
    }

    private string CleanDiscNameForSearch(string discName)
    {
        // Clean up disc name for OMDB searching by removing all disc/season/format identifiers
        var cleaned = discName
            .Replace("_", " ")
            .Replace("-", " ")
            .Trim();

        // Remove various disc/format identifiers
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\b(disc|disk|cd|dvd|bd|bluray|blu-ray)\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\b(season|s)\s*\d+\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\b[ds]\d+\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase); // Remove D1, D2, S8, etc.
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\b(part|pt)\s*\d+\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Remove multiple spaces and trim
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ").Trim();

        return cleaned;
    }
}

public enum MediaType
{
    Movie,
    TvSeries
}

public class EpisodeInfo
{
    public int Season { get; set; }
    public int Episode { get; set; }
}