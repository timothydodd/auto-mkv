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
/// Provides interactive console functionality for manual media identification when automatic lookup fails.
/// </summary>
public class ConsoleInteractionService
{
    private readonly IOmdbClient _omdbClient;
    private readonly ILogger<ConsoleInteractionService> _logger;
    private readonly IConsolePromptService _promptService;
    private readonly ISeriesConfigurationService _seriesConfigurationService;
    private bool _continueWithoutPrompting = false;

    public ConsoleInteractionService(IOmdbClient omdbClient, ILogger<ConsoleInteractionService> logger, IConsolePromptService promptService, ISeriesConfigurationService seriesConfigurationService)
    {
        _omdbClient = ValidationHelper.ValidateNotNull(omdbClient);
        _logger = ValidationHelper.ValidateNotNull(logger);
        _promptService = ValidationHelper.ValidateNotNull(promptService);
        _seriesConfigurationService = ValidationHelper.ValidateNotNull(seriesConfigurationService);
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

    private async Task<OptimizedSearchResult?> PerformInteractiveSearchAsync()
    {
        // First ask for media type
        var typeResult = _promptService.SelectPrompt<MediaType>(new SelectPromptOptions
        {
            Question = "What type of media is this?",
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
                    .Select(r => OptimizedSearchResult.FromOmdbSearchResult(r)).ToArray();
            }
        }
        else
        {
            // For movies, use regular search
            var searchArray = await _omdbClient.SearchMovie(titleResult.Value, year);
            // Filter to only movie results and convert
            searchResults = searchArray?.Where(r => r.Type?.Equals("movie", StringComparison.OrdinalIgnoreCase) == true)
                .Select(r => OptimizedSearchResult.FromOmdbSearchResult(r)).ToArray();
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

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("#")
            .AddColumn("Title")
            .AddColumn("Year")
            .AddColumn("Type");

        for (int i = 0; i < results.Length; i++)
        {
            var result = results[i];
            table.AddRow(
                (i + 1).ToString(),
                Markup.Escape(result.Title ?? "Unknown"),
                result.Year ?? "N/A",
                result.Type ?? "Unknown"
            );
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

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

            // Get full details for the selected item
            if (selected.Type?.Equals("series", StringComparison.OrdinalIgnoreCase) == true)
            {
                var seriesDetails = await _omdbClient.GetSeries(selected.Title!);
                return seriesDetails != null ? ModelConverter.ToOptimizedSearchResult(seriesDetails) : null;
            }
            else
            {
                var movieDetails = await _omdbClient.GetMovie(selected.Title!, int.TryParse(selected.Year, out var movieYear) ? movieYear : null);
                return movieDetails != null ? ModelConverter.ToOptimizedSearchResult(movieDetails) : null;
            }
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

    public enum ErrorHandlingChoice
    {
        Continue,
        ContinueWithoutPrompting,
        Exit
    }

    public ErrorHandlingChoice HandleEpisodeProcessingError(string seriesTitle, int season, int episode, string errorMessage)
    {
        // If user previously chose to continue without prompting, return that choice
        if (_continueWithoutPrompting)
        {
            _logger.LogInformation($"Continuing without prompt for error in {seriesTitle} S{season:D2}E{episode:D2}");
            return ErrorHandlingChoice.ContinueWithoutPrompting;
        }

        _promptService.DisplayHeader($"ERROR processing episode: {seriesTitle} - S{season:D2}E{episode:D2}");
        AnsiConsole.MarkupLine($"[red]Error:[/] [white]{Markup.Escape(errorMessage)}[/]");
        AnsiConsole.WriteLine();

        var result = _promptService.SelectPrompt<ErrorHandlingChoice>(new SelectPromptOptions
        {
            Question = "What would you like to do?",
            Choices = new List<PromptChoice>
            {
                new("continue", "Continue to next episode", ErrorHandlingChoice.Continue),
                new("continueNoPrompt", "Continue without prompting for future errors", ErrorHandlingChoice.ContinueWithoutPrompting),
                new("exit", "Exit application", ErrorHandlingChoice.Exit)
            }
        });

        if (!result.Success || result.Cancelled)
        {
            _logger.LogInformation("User cancelled error handling choice, defaulting to continue");
            return ErrorHandlingChoice.Continue;
        }

        switch (result.Value)
        {
            case ErrorHandlingChoice.Continue:
                _logger.LogInformation("User chose to continue to next episode");
                return ErrorHandlingChoice.Continue;

            case ErrorHandlingChoice.ContinueWithoutPrompting:
                _logger.LogInformation("User chose to continue without prompting for future errors");
                _continueWithoutPrompting = true;
                return ErrorHandlingChoice.ContinueWithoutPrompting;

            case ErrorHandlingChoice.Exit:
                _logger.LogInformation("User chose to exit application");
                return ErrorHandlingChoice.Exit;

            default:
                return ErrorHandlingChoice.Continue;
        }
    }

    public void ResetErrorPrompting()
    {
        _continueWithoutPrompting = false;
    }

    public enum FileAccessErrorChoice
    {
        Retry,
        Skip,
        Exit
    }

    public FileAccessErrorChoice HandleFileAccessError(string seriesTitle, int season, int episode, string fileName, string errorMessage)
    {
        _promptService.DisplayHeader($"FILE ACCESS ERROR - {seriesTitle} S{season:D2}E{episode:D2}");
        AnsiConsole.MarkupLine($"[dim]File:[/] [white]{Markup.Escape(fileName)}[/]");
        AnsiConsole.MarkupLine($"[red]Error:[/] [white]{Markup.Escape(errorMessage)}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]This file appears to be in use by another process or access is denied.[/]");
        AnsiConsole.MarkupLine("[dim]This can happen if another application is accessing the file.[/]");
        AnsiConsole.WriteLine();

        var result = _promptService.SelectPrompt<FileAccessErrorChoice>(new SelectPromptOptions
        {
            Question = "What would you like to do?",
            Choices = new List<PromptChoice>
            {
                new("retry", "Retry the operation", FileAccessErrorChoice.Retry),
                new("skip", "Skip this file and continue", FileAccessErrorChoice.Skip),
                new("exit", "Exit application", FileAccessErrorChoice.Exit)
            }
        });

        if (!result.Success || result.Cancelled)
        {
            _logger.LogInformation("User cancelled file access error handling, defaulting to skip");
            return FileAccessErrorChoice.Skip;
        }

        switch (result.Value)
        {
            case FileAccessErrorChoice.Retry:
                _logger.LogInformation($"User chose to retry file operation for {fileName}");
                return FileAccessErrorChoice.Retry;

            case FileAccessErrorChoice.Skip:
                _logger.LogInformation($"User chose to skip file: {fileName}");
                return FileAccessErrorChoice.Skip;

            case FileAccessErrorChoice.Exit:
                _logger.LogInformation("User chose to exit application due to file access error");
                return FileAccessErrorChoice.Exit;

            default:
                return FileAccessErrorChoice.Skip;
        }
    }

    public bool PromptForAutoIncrement(string seriesTitle, string discName)
    {
        _promptService.DisplayHeader($"Duplicate disc name detected for series: {seriesTitle}");
        AnsiConsole.MarkupLine($"[dim]Disc name:[/] [white]{Markup.Escape(discName)}[/]");
        AnsiConsole.WriteLine();

        var panel = new Panel(
            "[yellow]This disc name has been processed before for this series.[/]\n\n" +
            "Auto Increment mode assumes that each disc with the same name contains\n" +
            "the next sequential episodes in the series. When episode count exceeds\n" +
            "the current season, it will automatically move to the next season.")
        {
            Header = new PanelHeader("[cyan]Auto Increment Mode[/]"),
            Border = BoxBorder.Rounded
        };
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();

        var result = _promptService.SelectPrompt<bool>(new SelectPromptOptions
        {
            Question = "Would you like to enable Auto Increment mode?",
            Choices = new List<PromptChoice>
            {
                new("yes", "Yes - Enable Auto Increment mode for this series", true),
                new("no", "No - Continue with current episode numbering logic", false)
            }
        });

        if (!result.Success || result.Cancelled)
        {
            _logger.LogInformation($"User cancelled Auto Increment choice for series: {seriesTitle}, defaulting to false");
            return false;
        }

        _logger.LogInformation($"User {(result.Value ? "enabled" : "disabled")} Auto Increment mode for series: {seriesTitle}");
        return result.Value;
    }

    public bool PromptForAutoIncrementWhenEnabled(string seriesTitle, string discName)
    {
        _promptService.DisplayHeader($"Auto Increment mode is enabled for series: {seriesTitle}");
        AnsiConsole.MarkupLine($"[dim]Processing disc:[/] [white]{Markup.Escape(discName)}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[cyan]Auto Increment mode will automatically continue episode numbering from where[/]");
        AnsiConsole.MarkupLine("[cyan]the series left off, ignoring previous disc processing history.[/]");
        AnsiConsole.WriteLine();

        var result = _promptService.SelectPrompt<bool>(new SelectPromptOptions
        {
            Question = "Use Auto Increment mode?",
            Choices = new List<PromptChoice>
            {
                new("yes", "Yes - Use Auto Increment mode (skip disc check)", true),
                new("no", "No - Use standard processing (check if disc was processed before)", false)
            }
        });

        if (!result.Success || result.Cancelled)
        {
            _logger.LogInformation($"User cancelled Auto Increment choice for {seriesTitle}, defaulting to false");
            return false;
        }

        _logger.LogInformation($"User chose {(result.Value ? "Auto Increment" : "standard processing")} for {seriesTitle}");
        return result.Value;
    }

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

    public (int season, int episode) PromptForStartingSeasonAndEpisode(string seriesTitle, string discName)
    {
        _promptService.DisplayHeader("AUTOMATIC MODE - Season/Episode Information Needed");

        AnsiConsole.MarkupLine($"[dim]Series:[/] [white]{Markup.Escape(seriesTitle)}[/]");
        AnsiConsole.MarkupLine($"[dim]Disc:[/] [white]{Markup.Escape(discName)}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]This disc hasn't been processed before and the season/episode[/]");
        AnsiConsole.MarkupLine("[yellow]information cannot be determined automatically.[/]");
        AnsiConsole.WriteLine();

        var seasonResult = _promptService.NumberPrompt(new NumberPromptOptions
        {
            Question = "What season does this disc belong to?",
            Required = true,
            PromptText = "Season",
            MinValue = 1,
            MaxValue = 50
        });

        int season = seasonResult.Success ? seasonResult.Value : 1;

        var episodeResult = _promptService.NumberPrompt(new NumberPromptOptions
        {
            Question = $"What episode number should we start with for Season {season}?",
            Required = true,
            PromptText = "Episode",
            MinValue = 1,
            MaxValue = 100
        });

        int episode = episodeResult.Success ? episodeResult.Value : 1;

        AnsiConsole.MarkupLine($"[green]Starting with Season {season}, Episode {episode}[/]");

        _logger.LogInformation($"User specified starting point for {seriesTitle}: S{season:D2}E{episode:D2}");

        return (season, episode);
    }

    public (double? minSize, double? maxSize) PromptForEpisodeSizeRange(string seriesTitle)
    {
        return _seriesConfigurationService.PromptForEpisodeSizeRange(seriesTitle);
    }

    public (int? minChapters, int? maxChapters) PromptForEpisodeChapterRange(string seriesTitle)
    {
        return _seriesConfigurationService.PromptForEpisodeChapterRange(seriesTitle);
    }

    private (double minSize, double maxSize) PromptForSizeRange()
    {
        // Get minimum size
        var minSizeResult = _promptService.TextPrompt(new TextPromptOptions
        {
            Question = "Enter minimum episode size in GB (e.g., 0.5 for 500MB, 1.2 for 1.2GB):",
            Required = true,
            PromptText = "Min Size (GB)",
            ValidationPattern = @"^\d+(\.\d+)?$",
            ValidationMessage = "Please enter a valid number"
        });

        if (!minSizeResult.Success || !double.TryParse(minSizeResult.Value, out var minSize) || minSize <= 0 || minSize > 50)
        {
            _logger.LogWarning("Invalid minimum size entered, using default 0.5 GB");
            minSize = 0.5;
        }

        AnsiConsole.MarkupLine($"[green]Minimum episode size set to {minSize} GB[/]");

        // Get maximum size
        var maxSizeResult = _promptService.TextPrompt(new TextPromptOptions
        {
            Question = $"Enter maximum episode size in GB (must be >= {minSize} GB):",
            Required = true,
            PromptText = "Max Size (GB)",
            ValidationPattern = @"^\d+(\.\d+)?$",
            ValidationMessage = "Please enter a valid number"
        });

        if (!maxSizeResult.Success || !double.TryParse(maxSizeResult.Value, out var maxSize) || maxSize < minSize || maxSize > 100)
        {
            _logger.LogWarning($"Invalid maximum size entered, using {Math.Max(minSize * 2, 10)} GB");
            maxSize = Math.Max(minSize * 2, 10);
        }

        AnsiConsole.MarkupLine($"[green]Maximum episode size set to {maxSize} GB[/]");
        _logger.LogInformation($"User set episode size range: {minSize} - {maxSize} GB");
        return (minSize, maxSize);
    }

    private (int minChapters, int maxChapters) PromptForChapterRange()
    {
        // Get minimum chapters
        var minChaptersResult = _promptService.TextPrompt(new TextPromptOptions
        {
            Question = "Enter minimum episode chapter count (e.g., 1, 5, 10):",
            Required = true,
            PromptText = "Min Chapters",
            ValidationPattern = @"^\d+$",
            ValidationMessage = "Please enter a valid positive integer"
        });

        if (!minChaptersResult.Success || !int.TryParse(minChaptersResult.Value, out var minChapters) || minChapters <= 0 || minChapters > 999)
        {
            _logger.LogWarning("Invalid minimum chapter count entered, using default 1");
            minChapters = 1;
        }

        AnsiConsole.MarkupLine($"[green]Minimum episode chapters set to {minChapters}[/]");

        // Get maximum chapters
        var maxChaptersResult = _promptService.TextPrompt(new TextPromptOptions
        {
            Question = $"Enter maximum episode chapter count (must be >= {minChapters}):",
            Required = true,
            PromptText = "Max Chapters",
            ValidationPattern = @"^\d+$",
            ValidationMessage = "Please enter a valid positive integer"
        });

        if (!maxChaptersResult.Success || !int.TryParse(maxChaptersResult.Value, out var maxChapters) || maxChapters < minChapters || maxChapters > 999)
        {
            _logger.LogWarning($"Invalid maximum chapter count entered, using {Math.Max(minChapters * 2, 50)}");
            maxChapters = Math.Max(minChapters * 2, 50);
        }

        AnsiConsole.MarkupLine($"[green]Maximum episode chapters set to {maxChapters}[/]");
        _logger.LogInformation($"User set episode chapter range: {minChapters} - {maxChapters}");
        return (minChapters, maxChapters);
    }

    public TrackSortingStrategy PromptForTrackSortingStrategy(string seriesTitle)
    {
        return _seriesConfigurationService.PromptForTrackSortingStrategy(seriesTitle);
    }

    public (bool treatAsDouble, DoubleEpisodeHandling? savePreference) PromptForDoubleEpisodeHandling(
        string seriesTitle,
        string trackName,
        double trackLengthSeconds,
        double minEpisodeLengthSeconds)
    {
        return _seriesConfigurationService.PromptForDoubleEpisodeHandling(
            seriesTitle, trackName, trackLengthSeconds, minEpisodeLengthSeconds);
    }

    public SeriesProfile PromptForCompleteSeriesProfile(string seriesTitle, string discName)
    {
        return _seriesConfigurationService.PromptForCompleteSeriesProfile(seriesTitle, discName);
    }

    public SeriesProfile PromptForModifySeriesProfile(SeriesProfile existingProfile, string discName)
    {
        AnsiConsole.WriteLine();
        _promptService.DisplayHeader("MODIFY TV SERIES CONFIGURATION");
        AnsiConsole.MarkupLine($"[dim]Series:[/] [white]{Markup.Escape(existingProfile.SeriesTitle)}[/]");
        AnsiConsole.MarkupLine($"[dim]Disc:[/] [white]{Markup.Escape(discName)}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[cyan]You can modify individual settings or keep the existing values.[/]");
        AnsiConsole.MarkupLine("[dim]For each setting, you'll see the current value and can choose to keep it or change it.[/]");
        AnsiConsole.WriteLine();

        var modifiedProfile = new SeriesProfile
        {
            SeriesTitle = existingProfile.SeriesTitle,
            CreatedDate = existingProfile.CreatedDate,
            LastModifiedDate = DateTime.Now
        };

        // 1. Episode Size Range
        AnsiConsole.Write(new Rule("[yellow]SETTING 1/6: Episode Size Filtering[/]") { Justification = Justify.Left });
        if (existingProfile.MinEpisodeSizeGB.HasValue && existingProfile.MaxEpisodeSizeGB.HasValue)
        {
            AnsiConsole.MarkupLine($"[dim]Current:[/] Custom range [cyan]{existingProfile.MinEpisodeSizeGB:F1} - {existingProfile.MaxEpisodeSizeGB:F1} GB[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[dim]Current:[/] Using default filtering (global settings)");
        }
        AnsiConsole.WriteLine();

        var sizeSettingsResult = _promptService.SelectPrompt<bool>(new SelectPromptOptions
        {
            Question = "Keep current episode size settings?",
            Choices = new List<PromptChoice>
            {
                new("keep", "Keep current episode size settings", true),
                new("change", "Change episode size settings", false)
            }
        });

        if (!sizeSettingsResult.Success || sizeSettingsResult.Cancelled || sizeSettingsResult.Value)
        {
            modifiedProfile.MinEpisodeSizeGB = existingProfile.MinEpisodeSizeGB;
            modifiedProfile.MaxEpisodeSizeGB = existingProfile.MaxEpisodeSizeGB;
            _logger.LogInformation($"Kept existing episode size settings for {existingProfile.SeriesTitle}");
        }
        else
        {
            var (minSize, maxSize) = PromptForEpisodeSizeRange(existingProfile.SeriesTitle);
            modifiedProfile.MinEpisodeSizeGB = minSize;
            modifiedProfile.MaxEpisodeSizeGB = maxSize;
        }
        AnsiConsole.WriteLine();

        // 2. Track Sorting Strategy
        AnsiConsole.Write(new Rule("[yellow]SETTING 2/6: Track Sorting Method[/]") { Justification = Justify.Left });
        AnsiConsole.MarkupLine($"[dim]Current:[/] [cyan]{existingProfile.TrackSortingStrategy}[/]");
        AnsiConsole.WriteLine();

        var trackSortingResult = _promptService.SelectPrompt<bool>(new SelectPromptOptions
        {
            Question = "Keep current track sorting method?",
            Choices = new List<PromptChoice>
            {
                new("keep", "Keep current track sorting method", true),
                new("change", "Change track sorting method", false)
            }
        });

        if (!trackSortingResult.Success || trackSortingResult.Cancelled || trackSortingResult.Value)
        {
            modifiedProfile.TrackSortingStrategy = existingProfile.TrackSortingStrategy;
            _logger.LogInformation($"Kept existing track sorting strategy for {existingProfile.SeriesTitle}");
        }
        else
        {
            modifiedProfile.TrackSortingStrategy = PromptForTrackSortingStrategy(existingProfile.SeriesTitle);
        }
        AnsiConsole.WriteLine();

        // 3. Double Episode Handling
        AnsiConsole.Write(new Rule("[yellow]SETTING 3/6: Double Episode Detection[/]") { Justification = Justify.Left });
        AnsiConsole.MarkupLine($"[dim]Current:[/] [cyan]{existingProfile.DoubleEpisodeHandling}[/]");
        AnsiConsole.WriteLine();

        var doubleEpisodeKeepResult = _promptService.SelectPrompt<bool>(new SelectPromptOptions
        {
            Question = "Keep current double episode handling?",
            Choices = new List<PromptChoice>
            {
                new("keep", "Keep current double episode handling", true),
                new("change", "Change double episode handling", false)
            }
        });

        if (!doubleEpisodeKeepResult.Success || doubleEpisodeKeepResult.Cancelled || doubleEpisodeKeepResult.Value)
        {
            modifiedProfile.DoubleEpisodeHandling = existingProfile.DoubleEpisodeHandling;
            _logger.LogInformation($"Kept existing double episode handling for {existingProfile.SeriesTitle}");
        }
        else
        {
            AnsiConsole.WriteLine();
            var newDoubleHandlingResult = _promptService.SelectPrompt<DoubleEpisodeHandling>(new SelectPromptOptions
            {
                Question = "How should long episodes be handled?",
                Choices = new List<PromptChoice>
                {
                    new("ask", "Always ask me for each long episode (recommended)", DoubleEpisodeHandling.AlwaysAsk),
                    new("single", "Always treat as single episodes", DoubleEpisodeHandling.AlwaysSingle),
                    new("double", "Always treat long files as double episodes", DoubleEpisodeHandling.AlwaysDouble)
                }
            });

            if (newDoubleHandlingResult.Success && !newDoubleHandlingResult.Cancelled)
            {
                modifiedProfile.DoubleEpisodeHandling = newDoubleHandlingResult.Value;
                _logger.LogInformation($"Set double episode handling to {newDoubleHandlingResult.Value} for {existingProfile.SeriesTitle}");
            }
            else
            {
                modifiedProfile.DoubleEpisodeHandling = existingProfile.DoubleEpisodeHandling;
                _logger.LogInformation($"User cancelled double episode change, keeping existing setting for {existingProfile.SeriesTitle}");
            }
        }
        AnsiConsole.WriteLine();

        // 4. Starting Season/Episode (if not clear from disc name)
        AnsiConsole.Write(new Rule("[yellow]SETTING 4/6: Starting Position[/]") { Justification = Justify.Left });
        if (existingProfile.DefaultStartingSeason.HasValue && existingProfile.DefaultStartingEpisode.HasValue)
        {
            AnsiConsole.MarkupLine($"[dim]Current:[/] Default starting position [cyan]S{existingProfile.DefaultStartingSeason:D2}E{existingProfile.DefaultStartingEpisode:D2}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[dim]Current:[/] Extract from disc name automatically");
        }
        AnsiConsole.WriteLine();

        var startingPositionResult = _promptService.SelectPrompt<bool>(new SelectPromptOptions
        {
            Question = "Keep current starting position settings?",
            Choices = new List<PromptChoice>
            {
                new("keep", "Keep current starting position settings", true),
                new("change", "Change starting position settings", false)
            }
        });

        if (!startingPositionResult.Success || startingPositionResult.Cancelled || startingPositionResult.Value)
        {
            modifiedProfile.DefaultStartingSeason = existingProfile.DefaultStartingSeason;
            modifiedProfile.DefaultStartingEpisode = existingProfile.DefaultStartingEpisode;
            _logger.LogInformation($"Kept existing starting position settings for {existingProfile.SeriesTitle}");
        }
        else
        {
            if (!discName.Contains("S", StringComparison.OrdinalIgnoreCase) ||
                !discName.Contains("D", StringComparison.OrdinalIgnoreCase))
            {
                var (season, episode) = PromptForStartingSeasonAndEpisode(existingProfile.SeriesTitle, discName);
                modifiedProfile.DefaultStartingSeason = season;
                modifiedProfile.DefaultStartingEpisode = episode;
            }
            else
            {
                AnsiConsole.MarkupLine("[dim]Season/Episode information will be extracted from disc name.[/]");
                modifiedProfile.DefaultStartingSeason = null;
                modifiedProfile.DefaultStartingEpisode = null;
            }
        }
        AnsiConsole.WriteLine();

        // 5. Auto-increment mode
        AnsiConsole.Write(new Rule("[yellow]SETTING 5/6: Multi-Disc Handling[/]") { Justification = Justify.Left });
        AnsiConsole.MarkupLine($"[dim]Current:[/] Auto-increment [cyan]{(existingProfile.UseAutoIncrement ? "Enabled" : "Disabled")}[/]");
        AnsiConsole.WriteLine();

        var autoIncrementKeepResult = _promptService.SelectPrompt<bool>(new SelectPromptOptions
        {
            Question = "Keep current auto-increment setting?",
            Choices = new List<PromptChoice>
            {
                new("keep", "Keep current auto-increment setting", true),
                new("change", "Change auto-increment setting", false)
            }
        });

        if (!autoIncrementKeepResult.Success || autoIncrementKeepResult.Cancelled || autoIncrementKeepResult.Value)
        {
            modifiedProfile.UseAutoIncrement = existingProfile.UseAutoIncrement;
            _logger.LogInformation($"Kept existing auto-increment setting for {existingProfile.SeriesTitle}");
        }
        else
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]When processing multiple discs with similar names, should episode[/]");
            AnsiConsole.MarkupLine("[dim]numbers automatically continue from where the previous disc ended?[/]");
            AnsiConsole.WriteLine();

            var newAutoIncrementResult = _promptService.SelectPrompt<bool>(new SelectPromptOptions
            {
                Question = "Enable auto-increment mode?",
                Choices = new List<PromptChoice>
                {
                    new("yes", "Yes - Enable auto-increment mode", true),
                    new("no", "No - Always check disc processing history", false)
                }
            });

            if (newAutoIncrementResult.Success && !newAutoIncrementResult.Cancelled)
            {
                modifiedProfile.UseAutoIncrement = newAutoIncrementResult.Value;
                _logger.LogInformation($"{(newAutoIncrementResult.Value ? "Enabled" : "Disabled")} auto-increment mode for {existingProfile.SeriesTitle}");
            }
            else
            {
                modifiedProfile.UseAutoIncrement = existingProfile.UseAutoIncrement;
                _logger.LogInformation($"User cancelled auto-increment change, keeping existing setting for {existingProfile.SeriesTitle}");
            }
        }
        AnsiConsole.WriteLine();

        // 6. Confirmation preference
        AnsiConsole.Write(new Rule("[yellow]SETTING 6/6: Pre-Rip Confirmation[/]") { Justification = Justify.Left });
        AnsiConsole.MarkupLine($"[dim]Current:[/] Pre-rip confirmation [cyan]{(existingProfile.AlwaysSkipConfirmation ? "Disabled (auto-proceed)" : "Enabled (show confirmation)")}[/]");
        AnsiConsole.WriteLine();

        var confirmationKeepResult = _promptService.SelectPrompt<bool>(new SelectPromptOptions
        {
            Question = "Keep current confirmation preference?",
            Choices = new List<PromptChoice>
            {
                new("keep", "Keep current confirmation preference", true),
                new("change", "Change confirmation preference", false)
            }
        });

        if (!confirmationKeepResult.Success || confirmationKeepResult.Cancelled || confirmationKeepResult.Value)
        {
            modifiedProfile.AlwaysSkipConfirmation = existingProfile.AlwaysSkipConfirmation;
            _logger.LogInformation($"Kept existing confirmation preference for {existingProfile.SeriesTitle}");
        }
        else
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Would you like to review rip settings before each disc, or proceed[/]");
            AnsiConsole.MarkupLine("[dim]automatically with the configured settings?[/]");
            AnsiConsole.WriteLine();

            var newConfirmationResult = _promptService.SelectPrompt<bool>(new SelectPromptOptions
            {
                Question = "Show pre-rip confirmation for this series?",
                Choices = new List<PromptChoice>
                {
                    new("show", "Always show pre-rip confirmation (recommended)", false),
                    new("skip", "Skip confirmation and proceed automatically", true)
                }
            });

            if (newConfirmationResult.Success && !newConfirmationResult.Cancelled)
            {
                modifiedProfile.AlwaysSkipConfirmation = newConfirmationResult.Value;
                _logger.LogInformation($"{(newConfirmationResult.Value ? "Will skip" : "Will show")} pre-rip confirmation for {existingProfile.SeriesTitle}");
            }
            else
            {
                modifiedProfile.AlwaysSkipConfirmation = existingProfile.AlwaysSkipConfirmation;
                _logger.LogInformation($"User cancelled confirmation preference change, keeping existing setting for {existingProfile.SeriesTitle}");
            }
        }

        AnsiConsole.WriteLine();
        _promptService.DisplayHeader($"Settings modification complete! Updated settings will be used for this disc and saved for future discs from {existingProfile.SeriesTitle}.");

        return modifiedProfile;
    }

    public async Task<int> ConfirmOrSelectEpisodeAsync(string seriesTitle, int season, int suggestedEpisode, string episodeTitle, string trackName, List<int> availableEpisodes, IEnhancedOmdbService enhancedOmdbService, List<AkTitle>? allTracks = null)
    {
        AnsiConsole.WriteLine();
        _promptService.DisplayHeader("EPISODE CONFIRMATION");
        AnsiConsole.MarkupLine($"[dim]Series:[/] [white]{Markup.Escape(seriesTitle)}[/]");
        AnsiConsole.MarkupLine($"[dim]Track:[/] [white]{Markup.Escape(trackName)}[/]");
        AnsiConsole.WriteLine();

        if (!string.IsNullOrEmpty(episodeTitle))
        {
            AnsiConsole.MarkupLine($"[cyan]Suggested:[/] Episode {suggestedEpisode} - [white]\"{Markup.Escape(episodeTitle)}\"[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[cyan]Suggested:[/] Episode {suggestedEpisode} [dim](no title available)[/]");
        }

        AnsiConsole.WriteLine();

        var confirmResult = _promptService.SelectPrompt<bool>(new SelectPromptOptions
        {
            Question = "Confirm episode assignment?",
            Choices = new List<PromptChoice>
            {
                new("confirm", "Confirm - This is the correct episode", true),
                new("select", "Select different episode", false)
            }
        });

        if (!confirmResult.Success || confirmResult.Cancelled)
        {
            _logger.LogInformation($"User cancelled episode confirmation for track {trackName}, using suggested episode {suggestedEpisode}");
            return suggestedEpisode;
        }

        if (confirmResult.Value)
        {
            _logger.LogInformation($"User confirmed episode {suggestedEpisode} for track {trackName}");
            return suggestedEpisode;
        }

        return await SelectFromAvailableEpisodesAsync(seriesTitle, season, trackName, availableEpisodes, enhancedOmdbService, allTracks);
    }

    public async Task<int> SelectFromAvailableEpisodesAsync(string seriesTitle, int season, string trackName, List<int> availableEpisodes, IEnhancedOmdbService enhancedOmdbService, List<AkTitle>? allTracks = null)
    {
        AnsiConsole.WriteLine();
        _promptService.DisplayHeader("SELECT EPISODE");
        AnsiConsole.MarkupLine($"[dim]Series:[/] [white]{Markup.Escape(seriesTitle)}[/]");
        AnsiConsole.MarkupLine($"[dim]Track:[/] [white]{Markup.Escape(trackName)}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[cyan]Available episodes:[/]");
        AnsiConsole.WriteLine();

        var choices = new List<PromptChoice>();

        // Display available episodes with titles and MPLS info when available
        for (int i = 0; i < availableEpisodes.Count; i++)
        {
            var episodeNumber = availableEpisodes[i];
            string episodeTitle = "";
            string mplsInfo = "";

            try
            {
                var episodeInfo = await enhancedOmdbService.GetEpisodeInfoAsync(
                    seriesTitle,
                    season,
                    episodeNumber);

                if (episodeInfo != null && !string.IsNullOrEmpty(episodeInfo.Title))
                {
                    episodeTitle = $" - \"{episodeInfo.Title}\"";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Could not fetch title for episode {episodeNumber}");
            }

            // Try to find corresponding track with MPLS filename
            if (allTracks != null)
            {
                // Calculate the track index that would correspond to this episode
                // This assumes episodes are in order starting from the first available episode
                var trackIndex = episodeNumber - availableEpisodes.Min();
                if (trackIndex >= 0 && trackIndex < allTracks.Count)
                {
                    var correspondingTrack = allTracks[trackIndex];
                    if (!string.IsNullOrEmpty(correspondingTrack.SourceFileName))
                    {
                        mplsInfo = $" (MPLS: {correspondingTrack.SourceFileName})";
                    }
                }
            }

            var displayText = $"Episode {episodeNumber}{episodeTitle}{mplsInfo}";
            AnsiConsole.MarkupLine($"  [dim]{i + 1}.[/] {Markup.Escape(displayText)}");
            choices.Add(new(episodeNumber.ToString(), displayText, episodeNumber));
        }
        AnsiConsole.WriteLine();

        var selectionResult = _promptService.SelectPrompt<int>(new SelectPromptOptions
        {
            Question = "Select episode:",
            Choices = choices
        });

        if (!selectionResult.Success || selectionResult.Cancelled)
        {
            _logger.LogInformation($"User cancelled episode selection for track {trackName}, using first available episode");
            return availableEpisodes.First();
        }

        _logger.LogInformation($"User selected episode {selectionResult.Value} for track {trackName}");
        return selectionResult.Value;
    }

    public RipConfirmationResult ConfirmRipSettings(RipConfirmation confirmation)
    {
        AnsiConsole.WriteLine();
        _promptService.DisplayHeader("PRE-RIP CONFIRMATION");

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[cyan]Setting[/]")
            .AddColumn("[white]Value[/]");

        table.AddRow("Media", Markup.Escape(confirmation.MediaTitle));
        table.AddRow("Type", confirmation.MediaType);
        table.AddRow("Tracks to rip", $"{confirmation.TracksToRip} tracks");

        if (confirmation.MediaType.Equals("TV Series", StringComparison.OrdinalIgnoreCase))
        {
            table.AddRow("Episode filter", $"{confirmation.MinSizeGB:F1} - {confirmation.MaxSizeGB:F1} GB");
            table.AddRow("Chapter filter", $"{confirmation.MinChapters} - {confirmation.MaxChapters} chapters");
            table.AddRow("Track sorting", confirmation.SortingMethod);
            table.AddRow("Starting at", confirmation.StartingPosition);
            table.AddRow("Double episodes", confirmation.DoubleEpisodeHandling);
        }
        else
        {
            table.AddRow("Selected track size", $"{confirmation.MinSizeGB:F1} GB");
            table.AddRow("Chapter filter", $"{confirmation.MinChapters} - {confirmation.MaxChapters} chapters");
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[cyan]Selected tracks:[/]");

        foreach (var track in confirmation.SelectedTracks.Take(5))
        {
            AnsiConsole.MarkupLine($"  [dim]-[/] {Markup.Escape(track.Name)} [dim]({track.SizeInGB:F2} GB, {track.LengthInSeconds / 60.0:F1} min, {track.ChapterCount} chapters)[/]");
        }
        if (confirmation.SelectedTracks.Count > 5)
        {
            AnsiConsole.MarkupLine($"  [dim]... and {confirmation.SelectedTracks.Count - 5} more tracks[/]");
        }

        AnsiConsole.WriteLine();

        var choices = new List<PromptChoice>
        {
            new("proceed", "Proceed with ripping", RipConfirmationResult.Proceed),
            new("modify", "Modify settings", RipConfirmationResult.ModifySettings),
            new("skip", "Skip this disc", RipConfirmationResult.Skip)
        };

        if (confirmation.MediaType.Equals("TV Series", StringComparison.OrdinalIgnoreCase))
        {
            choices.Add(new("proceed_no_ask", "Proceed and don't ask again for this series", RipConfirmationResult.ProceedAndDontAskAgain));
        }

        var confirmationResult = _promptService.SelectPrompt<RipConfirmationResult>(new SelectPromptOptions
        {
            Question = "What would you like to do?",
            Choices = choices
        });

        if (!confirmationResult.Success || confirmationResult.Cancelled)
        {
            _logger.LogInformation("User cancelled rip confirmation, defaulting to skip");
            return RipConfirmationResult.Skip;
        }

        _logger.LogInformation($"User chose to {confirmationResult.Value.ToString().ToLower()}");
        return confirmationResult.Value;
    }

    public PromptResult<bool> PromptForSeasonMismatchResolution(string seriesTitle, string discName, int currentSeason, int discSeason)
    {
        _promptService.DisplayHeader("SEASON MISMATCH DETECTED", '!');

        // Display warning before the details
        _promptService.DisplayWarning($"Season mismatch detected for {seriesTitle}!");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine($"[dim]Series:[/] [white]{Markup.Escape(seriesTitle)}[/]");
        AnsiConsole.MarkupLine($"[dim]Disc:[/] [white]{Markup.Escape(discName)}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[dim]Current series state shows[/] [cyan]Season {currentSeason}[/]");
        AnsiConsole.MarkupLine($"[dim]This disc appears to be[/] [yellow]Season {discSeason}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]This might indicate you're starting a new season or the disc label[/]");
        AnsiConsole.MarkupLine("[dim]has been parsed incorrectly.[/]");
        AnsiConsole.WriteLine();

        var result = _promptService.SelectPrompt<bool>(new SelectPromptOptions
        {
            Question = "What would you like to do?",
            Choices = new List<PromptChoice>
            {
                new("update", $"Update to Season {discSeason} and reset episode counter", true),
                new("continue", $"Continue with current Season {currentSeason}", false)
            }
        });

        if (result.Success)
        {
            var action = result.Value ? "update season" : "continue with current season";
            _logger.LogInformation($"User chose to {action} for {seriesTitle}");
        }
        else
        {
            _logger.LogWarning($"User cancelled season mismatch resolution for {seriesTitle}");
        }

        return result;
    }
}
