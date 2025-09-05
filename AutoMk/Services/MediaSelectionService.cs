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
        Console.WriteLine($"Original search: {originalTitle}");
        Console.WriteLine();

        while (true)
        {
            var result = _promptService.SelectPrompt(new SelectPromptOptions
            {
                Question = "What would you like to do?",
                Choices = new List<PromptChoice>
                {
                    new("search", "Search for a movie or TV series"),
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
        
        Console.WriteLine($"Disc: {discName}");
        Console.WriteLine($"Identified as: {mediaData.Title} ({mediaData.Year})");
        Console.WriteLine($"Type: {mediaData.Type?.ToUpperInvariant()}");
        if (!string.IsNullOrEmpty(mediaData.Plot))
        {
            Console.WriteLine($"Plot: {mediaData.Plot}");
        }
        Console.WriteLine();

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
        Console.WriteLine($"Searching for {(mediaType == MediaTypePrediction.Movie ? "movie" : "TV series")}: {searchQuery}");
        Console.WriteLine();

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
            Console.WriteLine($"No {(mediaType == MediaTypePrediction.Movie ? "movies" : "TV series")} found matching '{searchQuery}'.");
            Console.WriteLine();
            return null;
        }

        return searchResults;
    }

    public OptimizedSearchResult? SelectFromSearchResults(List<OptimizedSearchResult> searchResults, string searchQuery)
    {
        if (searchResults == null || searchResults.Count == 0)
        {
            Console.WriteLine($"No results found for '{searchQuery}'.");
            return null;
        }

        _promptService.DisplayHeader("Search Results");
        
        for (int i = 0; i < searchResults.Count; i++)
        {
            var result = searchResults[i];
            Console.WriteLine($"{i + 1}. {result.Title} ({result.Year}) - {result.Type}");
        }
        
        Console.WriteLine($"{searchResults.Count + 1}. None of these (search again)");
        Console.WriteLine();

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
            Console.WriteLine($"Selected: {selected.Title} ({selected.Year})");
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

        Console.WriteLine($"Searching for {(isMovie ? "movie" : "TV series")}: {titleResult.Value}" + (year.HasValue ? $" ({year})" : ""));
        Console.WriteLine();

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
            Console.WriteLine($"No {(isMovie ? "movies" : "TV series")} found matching '{titleResult.Value}'.");
            Console.WriteLine();
            return null;
        }

        return await DisplayAndSelectResultAsync(searchResults);
    }

    private async Task<OptimizedSearchResult?> DisplayAndSelectResultAsync(OptimizedSearchResult[] results)
    {
        _promptService.DisplayHeader("Search Results");
        
        for (int i = 0; i < results.Length; i++)
        {
            var result = results[i];
            Console.WriteLine($"{i + 1}. {result.Title} ({result.Year}) - {result.Type}");
        }
        
        Console.WriteLine($"{results.Length + 1}. Search again");
        Console.WriteLine($"{results.Length + 2}. Skip this disc");
        Console.WriteLine();

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
            Console.WriteLine($"Selected: {selected.Title} ({selected.Year})");
            
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
    /// Resolves conflicts when both movie and series matches are found
    /// </summary>
    /// <param name="discName">The name of the disc being processed</param>
    /// <param name="movieResult">The movie match found</param>
    /// <param name="seriesResult">The series match found</param>
    /// <returns>The selected media data or null if cancelled</returns>
    public async Task<MediaIdentity?> SelectBetweenMovieAndSeriesAsync(string discName, ConfirmationInfo movieResult, ConfirmationInfo seriesResult)
    {
        _promptService.DisplayHeader($"Multiple media types found for disc: {discName}");
        Console.WriteLine("Found both movie and TV series matches:");
        Console.WriteLine();
        Console.WriteLine($"MOVIE: {movieResult.Title} ({movieResult.Year})");
        if (!string.IsNullOrEmpty(movieResult.Plot))
        {
            Console.WriteLine($"   Plot: {movieResult.Plot}");
        }
        Console.WriteLine();
        Console.WriteLine($"TV SERIES: {seriesResult.Title} ({seriesResult.Year})");
        if (!string.IsNullOrEmpty(seriesResult.Plot))
        {
            Console.WriteLine($"   Plot: {seriesResult.Plot}");
        }
        Console.WriteLine();
        Console.WriteLine("Choose which media type is correct:");
        Console.WriteLine();

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