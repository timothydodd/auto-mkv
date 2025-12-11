using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMk.Extensions;
using AutoMk.Interfaces;
using AutoMk.Models;
using AutoMk.Utilities;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace AutoMk.Services;

/// <summary>
/// Service for handling media search and selection UI interactions
/// </summary>
public class MediaSelectionService : IMediaSelectionService
{
    private readonly IOmdbClient _omdbClient;
    private readonly ILogger<MediaSelectionService> _logger;
    private readonly IConsolePromptService _promptService;

    public MediaSelectionService(IOmdbClient omdbClient, ILogger<MediaSelectionService> logger, IConsolePromptService promptService)
    {
        _omdbClient = ValidationHelper.ValidateNotNull(omdbClient);
        _logger = ValidationHelper.ValidateNotNull(logger);
        _promptService = ValidationHelper.ValidateNotNull(promptService);
    }

    public async Task<OptimizedSearchResult?> InteractiveMediaSearchAsync(string originalTitle, string discName)
    {
        _promptService.DisplayHeader($"Unable to automatically identify media for disc: {discName}");
        AnsiConsole.MarkupLine($"[dim]Original search:[/] [white]{Markup.Escape(originalTitle)}[/]");
        AnsiConsole.WriteLine();

        while (true)
        {
            var result = _promptService.SelectPrompt(new SelectPromptOptions
            {
                Question = "What would you like to do?",
                Choices = new List<PromptChoice>
                {
                    new("search", "Search for a movie or TV series"),
                    new("imdb", "Search by IMDB ID (e.g., tt1285016)"),
                    new("manual", "Manual entry (enter title/year directly)"),
                    new("skip", "Skip this disc (continue without renaming)")
                }
            });

            if (!result.Success || result.Cancelled)
            {
                _logger.LogInformation("User cancelled media search");
                return null;
            }

            switch (result.Value)
            {
                case "search":
                    var searchResult = await PerformInteractiveSearchAsync();
                    if (searchResult != null)
                        return searchResult;
                    break;

                case "imdb":
                    var imdbResult = await PerformImdbIdSearchAsync();
                    if (imdbResult != null)
                        return imdbResult;
                    break;

                case "manual":
                    var manualResult = PerformManualEntryAsync();
                    if (manualResult != null)
                        return manualResult;
                    break;

                case "skip":
                    _logger.LogInformation("User chose to skip disc without identification");
                    return null;

                default:
                    break;
            }
        }
    }

    public bool ConfirmMediaIdentification(ConfirmationInfo mediaData, string discName)
    {
        _promptService.DisplayHeader("AUTOMATIC MODE - Media Identification Confirmation");

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Property")
            .AddColumn("Value");

        table.AddRow("[dim]Disc[/]", Markup.Escape(discName));
        table.AddRow("[dim]Identified as[/]", $"[white]{Markup.Escape(mediaData.Title ?? "Unknown")} ({mediaData.Year})[/]");
        table.AddRow("[dim]Type[/]", $"[cyan]{(mediaData.Type?.ToUpperInvariant() ?? "UNKNOWN")}[/]");
        if (!string.IsNullOrEmpty(mediaData.Plot))
        {
            table.AddRow("[dim]Plot[/]", Markup.Escape(mediaData.Plot));
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        var result = _promptService.ConfirmPrompt(new ConfirmPromptOptions
        {
            Question = "Is this identification correct?",
            DefaultValue = true
        });

        if (result.Success)
        {
            if (result.Value)
            {
                _logger.LogInformation($"User confirmed identification: {mediaData.Title}");
                return true;
            }
            else
            {
                _logger.LogInformation($"User rejected identification: {mediaData.Title}");
                return false;
            }
        }

        // Default to false if cancelled or error
        _logger.LogInformation($"User cancelled identification confirmation for: {mediaData.Title}");
        return false;
    }

    public async Task<OptimizedSearchResult[]?> SearchMediaByTitleAsync(string searchQuery, MediaTypePrediction mediaType)
    {
        AnsiConsole.MarkupLine($"[cyan]Searching for {(mediaType == MediaTypePrediction.Movie ? "movie" : "TV series")}:[/] [white]{Markup.Escape(searchQuery)}[/]");
        AnsiConsole.WriteLine();

        // Search based on type
        OptimizedSearchResult[]? searchResults;
        
        if (mediaType == MediaTypePrediction.TvSeries)
        {
            // For TV series, try direct lookup first
            var seriesResult = await _omdbClient.GetSeries(searchQuery);
            if (seriesResult.IsValidOmdbResponse())
            {
                // If direct lookup succeeds, wrap it in an array for display
                searchResults = new[] { ModelConverter.ToOptimizedSearchResult(seriesResult) };
            }
            else
            {
                // Fall back to search
                var searchArray = await _omdbClient.SearchMovie(searchQuery, null);
                // Filter to only series results and convert
                searchResults = searchArray?.Where(r => r.Type?.Equals("series", StringComparison.OrdinalIgnoreCase) == true)
                    .Select(ModelConverter.ToOptimizedSearchResult).ToArray();
            }
        }
        else
        {
            // For movies, use regular search
            var searchArray = await _omdbClient.SearchMovie(searchQuery, null);
            // Filter to only movie results and convert
            searchResults = searchArray?.Where(r => r.Type?.Equals("movie", StringComparison.OrdinalIgnoreCase) == true)
                .Select(ModelConverter.ToOptimizedSearchResult).ToArray();
        }
        
        if (searchResults == null || searchResults.Length == 0)
        {
            AnsiConsole.MarkupLine($"[yellow]No {(mediaType == MediaTypePrediction.Movie ? "movies" : "TV series")} found matching[/] [white]'{Markup.Escape(searchQuery)}'[/]");
            AnsiConsole.WriteLine();
            return null;
        }

        return searchResults;
    }

    public OptimizedSearchResult? SelectFromSearchResults(List<OptimizedSearchResult> searchResults, string searchQuery)
    {
        if (searchResults == null || searchResults.Count == 0)
        {
            AnsiConsole.MarkupLine($"[yellow]No results found for[/] [white]'{Markup.Escape(searchQuery)}'[/]");
            return null;
        }

        _promptService.DisplayHeader("Search Results");

        var choices = new List<PromptChoice>();
        for (int i = 0; i < searchResults.Count; i++)
        {
            var result = searchResults[i];
            choices.Add(new((i + 1).ToString(), $"{result.Title} ({result.Year}) - {result.Type}", i + 1));
        }
        choices.Add(new("none", "None of these (search again)", searchResults.Count + 1));

        var selectionResult = _promptService.SelectPrompt<int>(new SelectPromptOptions
        {
            Question = "Select an option:",
            Choices = choices
        });

        if (!selectionResult.Success || selectionResult.Cancelled)
        {
            _logger.LogInformation("User cancelled search result selection");
            return null;
        }

        var selection = selectionResult.Value;
        if (selection >= 1 && selection <= searchResults.Count)
        {
            var selected = searchResults[selection - 1];
            AnsiConsole.MarkupLine($"[green]Selected:[/] [white]{Markup.Escape(selected.Title ?? "Unknown")} ({selected.Year})[/]");
            return selected;
        }
        else if (selection == searchResults.Count + 1)
        {
            // None of these - search again
            return null;
        }

        return null;
    }

    private async Task<OptimizedSearchResult?> PerformInteractiveSearchAsync()
    {
        // First ask for media type
        var typeResult = _promptService.SelectPrompt<MediaTypePrediction>(new SelectPromptOptions
        {
            Question = "What type of media is this?",
            Choices = new List<PromptChoice>
            {
                new("movie", "Movie", MediaTypePrediction.Movie),
                new("series", "TV Series", MediaTypePrediction.TvSeries)
            }
        });

        if (!typeResult.Success || typeResult.Cancelled)
        {
            return null;
        }

        bool isMovie = typeResult.Value == MediaTypePrediction.Movie;
        
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

        AnsiConsole.MarkupLine($"[cyan]Searching for {(isMovie ? "movie" : "TV series")}:[/] [white]{Markup.Escape(titleResult.Value!)}[/]" + (year.HasValue ? $" [dim]({year})[/]" : ""));
        AnsiConsole.WriteLine();

        // Search based on type
        OptimizedSearchResult[]? searchResults;
        
        if (!isMovie)
        {
            // For TV series, try direct lookup first
            var seriesResult = await _omdbClient.GetSeries(titleResult.Value);
            if (seriesResult.IsValidOmdbResponse())
            {
                // If direct lookup succeeds, wrap it in an array for display
                searchResults = new[] { ModelConverter.ToOptimizedSearchResult(seriesResult) };
            }
            else
            {
                // Fall back to search
                var searchArray = await _omdbClient.SearchMovie(titleResult.Value, year);
                // Filter to only series results and convert
                searchResults = searchArray?.Where(r => r.Type?.Equals("series", StringComparison.OrdinalIgnoreCase) == true)
                    .Select(ModelConverter.ToOptimizedSearchResult).ToArray();
            }
        }
        else
        {
            // For movies, use regular search
            var searchArray = await _omdbClient.SearchMovie(titleResult.Value, year);
            // Filter to only movie results and convert
            searchResults = searchArray?.Where(r => r.Type?.Equals("movie", StringComparison.OrdinalIgnoreCase) == true)
                .Select(ModelConverter.ToOptimizedSearchResult).ToArray();
        }
        
        if (searchResults == null || searchResults.Length == 0)
        {
            AnsiConsole.MarkupLine($"[yellow]No {(isMovie ? "movies" : "TV series")} found matching[/] [white]'{Markup.Escape(titleResult.Value!)}'[/]");
            AnsiConsole.WriteLine();
            return null;
        }

        return await DisplayAndSelectResultAsync(searchResults);
    }

    private async Task<OptimizedSearchResult?> DisplayAndSelectResultAsync(OptimizedSearchResult[] results)
    {
        _promptService.DisplayHeader("Search Results");

        var choices = new List<PromptChoice>();
        for (int i = 0; i < results.Length; i++)
        {
            var result = results[i];
            choices.Add(new((i + 1).ToString(), $"{result.Title} ({result.Year}) - {result.Type}", i + 1));
        }
        choices.Add(new("search_again", "Search again", results.Length + 1));
        choices.Add(new("skip", "Skip this disc", results.Length + 2));

        var selectionResult = _promptService.SelectPrompt<int>(new SelectPromptOptions
        {
            Question = "Select an option:",
            Choices = choices
        });

        if (!selectionResult.Success || selectionResult.Cancelled)
        {
            _logger.LogInformation("User cancelled search result selection");
            return null;
        }

        var selection = selectionResult.Value;
        if (selection >= 1 && selection <= results.Length)
        {
            var selected = results[selection - 1];
            AnsiConsole.MarkupLine($"[green]Selected:[/] [white]{Markup.Escape(selected.Title ?? "Unknown")} ({selected.Year})[/]");
            
            // Return the selected result directly - callers will fetch full details as needed
            return selected;
        }
        else if (selection == results.Length + 1)
        {
            // Search again
            return null;
        }
        else if (selection == results.Length + 2)
        {
            // Skip
            _logger.LogInformation("User chose to skip after viewing search results");
            return null;
        }

        return null;
    }

    /// <summary>
    /// Handles searching for media by IMDB ID
    /// </summary>
    /// <returns>Media information from OMDB or null if cancelled/not found</returns>
    private async Task<OptimizedSearchResult?> PerformImdbIdSearchAsync()
    {
        _promptService.DisplayHeader("Search by IMDB ID");
        AnsiConsole.MarkupLine("[dim]Enter an IMDB ID to lookup media information.[/]");
        AnsiConsole.MarkupLine("[dim]Format: tt followed by numbers (e.g., tt1285016, tt0108778)[/]");
        AnsiConsole.WriteLine();

        while (true)
        {
            // Get IMDB ID from user
            var imdbIdResult = _promptService.TextPrompt(new TextPromptOptions
            {
                Question = "Enter IMDB ID:",
                Required = true,
                PromptText = "IMDB ID",
                ValidationPattern = @"^tt\d+$",
                ValidationMessage = "IMDB ID must be in format 'tt' followed by numbers (e.g., tt1285016)"
            });

            if (!imdbIdResult.Success || imdbIdResult.Cancelled || string.IsNullOrWhiteSpace(imdbIdResult.Value))
            {
                return null;
            }

            var imdbId = imdbIdResult.Value.Trim();

            AnsiConsole.MarkupLine($"[cyan]Looking up IMDB ID:[/] [white]{imdbId}[/]");
            AnsiConsole.WriteLine();

            // Fetch from OMDB
            var mediaResponse = await _omdbClient.GetMediaByImdbId(imdbId);

            if (mediaResponse == null || !mediaResponse.IsValidOmdbResponse())
            {
                _promptService.DisplayError($"No media found for IMDB ID: {imdbId}");
                AnsiConsole.WriteLine();

                // Ask if they want to try again
                var retryResult = _promptService.ConfirmPrompt(new ConfirmPromptOptions
                {
                    Question = "Would you like to try a different IMDB ID?",
                    DefaultValue = true
                });

                if (!retryResult.Success || !retryResult.Value)
                {
                    return null;
                }

                continue;
            }

            // Convert to OptimizedSearchResult
            var result = ModelConverter.ToOptimizedSearchResult(mediaResponse);

            AnsiConsole.MarkupLine($"[green]Found:[/] [white]{Markup.Escape(result.Title ?? "Unknown")} ({result.Year})[/] [dim]- {result.Type?.ToUpperInvariant()}[/]");
            _logger.LogInformation($"IMDB ID search successful: {imdbId} -> {result.Title} ({result.Year})");

            return result;
        }
    }

    /// <summary>
    /// Handles manual entry of media information without OMDB search
    /// </summary>
    /// <returns>Manually entered media information or null if cancelled</returns>
    private OptimizedSearchResult? PerformManualEntryAsync()
    {
        _promptService.DisplayHeader("Manual Media Entry");
        AnsiConsole.MarkupLine("[dim]Enter media information directly (no OMDB search)[/]");
        AnsiConsole.WriteLine();

        // First ask for media type
        var typeResult = _promptService.SelectPrompt<MediaTypePrediction>(new SelectPromptOptions
        {
            Question = "What type of media is this?",
            Choices = new List<PromptChoice>
            {
                new("movie", "Movie", MediaTypePrediction.Movie),
                new("series", "TV Series", MediaTypePrediction.TvSeries)
            }
        });

        if (!typeResult.Success || typeResult.Cancelled)
        {
            return null;
        }

        bool isMovie = typeResult.Value == MediaTypePrediction.Movie;
        string mediaTypeString = isMovie ? "movie" : "series";

        // Get title (required)
        var titleResult = _promptService.TextPrompt(new TextPromptOptions
        {
            Question = $"Enter {(isMovie ? "movie" : "TV series")} title:",
            Required = true,
            PromptText = "Title"
        });

        if (!titleResult.Success || titleResult.Cancelled || string.IsNullOrWhiteSpace(titleResult.Value))
        {
            return null;
        }

        // Get year (required for manual entry)
        var yearResult = _promptService.NumberPrompt(new NumberPromptOptions
        {
            Question = "Enter year:",
            Required = true,
            PromptText = "Year",
            MinValue = 1900,
            MaxValue = DateTime.Now.Year + 5
        });

        if (!yearResult.Success || yearResult.Cancelled)
        {
            return null;
        }

        // Create the manually entered result
        var manualResult = new OptimizedSearchResult
        {
            Title = titleResult.Value.Trim(),
            Year = yearResult.Value.ToString(),
            Type = mediaTypeString,
            ImdbID = null, // Manual entries won't have IMDB ID
            Poster = null
        };

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[green]Created manual entry:[/] [white]{Markup.Escape(manualResult.Title)} ({manualResult.Year})[/] [dim]- {manualResult.Type.ToUpperInvariant()}[/]");
        _logger.LogInformation($"Manual entry created: {manualResult.Title} ({manualResult.Year}) - {manualResult.Type}");

        return manualResult;
    }

    /// <summary>
    /// Resolves conflicts when both movie and series matches are found
    /// </summary>
    /// <param name="discName">The name of the disc being processed</param>
    /// <param name="movieResult">The movie match found</param>
    /// <param name="seriesResult">The series match found</param>
    /// <returns>The selected media data or null if cancelled</returns>
    public async Task<MediaIdentity?> SelectBetweenMovieAndSeriesAsync(string discName, ConfirmationInfo movieResult, ConfirmationInfo seriesResult)
    {
        _promptService.DisplayHeader($"Multiple media types found for disc: {discName}");
        AnsiConsole.MarkupLine("[yellow]Found both movie and TV series matches:[/]");
        AnsiConsole.WriteLine();

        // Movie panel
        var movieContent = $"[white]{Markup.Escape(movieResult.Title ?? "Unknown")} ({movieResult.Year})[/]";
        if (!string.IsNullOrEmpty(movieResult.Plot))
        {
            movieContent += $"\n[dim]{Markup.Escape(movieResult.Plot)}[/]";
        }
        var moviePanel = new Panel(movieContent) { Header = new PanelHeader("[blue]MOVIE[/]"), Border = BoxBorder.Rounded };
        AnsiConsole.Write(moviePanel);
        AnsiConsole.WriteLine();

        // Series panel
        var seriesContent = $"[white]{Markup.Escape(seriesResult.Title ?? "Unknown")} ({seriesResult.Year})[/]";
        if (!string.IsNullOrEmpty(seriesResult.Plot))
        {
            seriesContent += $"\n[dim]{Markup.Escape(seriesResult.Plot)}[/]";
        }
        var seriesPanel = new Panel(seriesContent) { Header = new PanelHeader("[green]TV SERIES[/]"), Border = BoxBorder.Rounded };
        AnsiConsole.Write(seriesPanel);
        AnsiConsole.WriteLine();

        while (true)
        {
            var result = _promptService.SelectPrompt(new SelectPromptOptions
            {
                Question = "Choose which media type is correct:",
                Choices = new List<PromptChoice>
                {
                    new("movie", $"MOVIE: {movieResult.Title} ({movieResult.Year})"),
                    new("series", $"TV SERIES: {seriesResult.Title} ({seriesResult.Year})"),
                    new("search", "Search for something else"),
                    new("skip", "Skip identification (continue without renaming)")
                }
            });

            if (!result.Success || result.Cancelled)
            {
                _logger.LogInformation("User cancelled media type selection");
                return null;
            }

            switch (result.Value)
            {
                case "movie":
                    _logger.LogInformation($"User selected movie: {movieResult.Title}");
                    return ModelConverter.ToMediaIdentity(movieResult);
                    
                case "series":
                    _logger.LogInformation($"User selected TV series: {seriesResult.Title}");
                    return ModelConverter.ToMediaIdentity(seriesResult);
                    
                case "search":
                    var searchResult = await PerformInteractiveSearchAsync();
                    if (searchResult != null)
                        return ModelConverter.ToMediaIdentity(searchResult);
                    break;
                    
                case "skip":
                    _logger.LogInformation("User chose to skip identification");
                    return null;
            }
        }
    }
}