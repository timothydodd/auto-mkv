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
        AnsiConsole.WriteLine();
        _promptService.DisplayHeader($"MANUAL MODE - Media Identification for disc: {discName}");

        // First, try automatic identification
        var searchTitle = CleanDiscNameForSearch(discName);
        AnsiConsole.MarkupLine($"[dim]Attempting automatic identification for:[/] [white]'{Markup.Escape(searchTitle)}'[/]");
        AnsiConsole.WriteLine();

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
            AnsiConsole.MarkupLine("[green]Automatic identification found the following results:[/]");
            AnsiConsole.WriteLine();

            if (foundMovie)
            {
                var moviePanel = new Panel(
                    new Markup($"[white]{Markup.Escape(movieResult.Title ?? "")}[/] [dim]({Markup.Escape(movieResult.Year ?? "")})[/]\n\n" +
                              $"[dim]{Markup.Escape(movieResult.Plot ?? "")}[/]"))
                {
                    Header = new PanelHeader("[yellow]MOVIE[/]"),
                    Border = BoxBorder.Rounded,
                    BorderStyle = new Style(Color.Yellow)
                };
                AnsiConsole.Write(moviePanel);
                AnsiConsole.WriteLine();
            }

            if (foundSeries)
            {
                var seriesPanel = new Panel(
                    new Markup($"[white]{Markup.Escape(seriesResult.Title ?? "")}[/] [dim]({Markup.Escape(seriesResult.Year ?? "")})[/]\n\n" +
                              $"[dim]{Markup.Escape(seriesResult.Plot ?? "")}[/]"))
                {
                    Header = new PanelHeader("[blue]TV SERIES[/]"),
                    Border = BoxBorder.Rounded,
                    BorderStyle = new Style(Color.Blue)
                };
                AnsiConsole.Write(seriesPanel);
                AnsiConsole.WriteLine();
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
        AnsiConsole.MarkupLine("[yellow]No automatic results found. Please search manually.[/]");
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
        AnsiConsole.WriteLine();
        _promptService.DisplayHeader("MANUAL MODE - Track Selection");

        // Display available tracks in a table
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn(new TableColumn("[white]#[/]").Centered())
            .AddColumn(new TableColumn("[white]Track ID[/]").Centered())
            .AddColumn(new TableColumn("[white]Name[/]"))
            .AddColumn(new TableColumn("[white]Duration[/]").Centered())
            .AddColumn(new TableColumn("[white]Size[/]").RightAligned());

        for (int i = 0; i < availableTitles.Count; i++)
        {
            var title = availableTitles[i];
            table.AddRow(
                $"[cyan]{i + 1}[/]",
                title.Id?.ToString() ?? "-",
                Markup.Escape(title.Name ?? "Unknown"),
                title.Length ?? "-",
                $"{title.SizeInGB:F2} GB"
            );
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        // Use MultiSelectionPrompt for track selection with arrow keys
        var choices = availableTitles.Select((t, i) => $"{i + 1}. Track {t.Id}: {t.Name} ({t.SizeInGB:F2} GB)").ToList();

        var selectedChoices = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title("[white]Select tracks to rip:[/]")
                .PageSize(15)
                .HighlightStyle(new Style(Color.Cyan1))
                .InstructionsText("[dim](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept)[/]")
                .AddChoiceGroup("[green]Select All[/]", choices)
                .NotRequired());

        // Filter out the "Select All" group header if selected
        var selectedIndices = selectedChoices
            .Where(c => c != "[green]Select All[/]")
            .Select(c =>
            {
                var numStr = c.Split('.')[0];
                return int.TryParse(numStr, out var num) ? num - 1 : -1;
            })
            .Where(i => i >= 0 && i < availableTitles.Count)
            .ToList();

        var selectedTracks = selectedIndices.Select(i => availableTitles[i]).ToList();

        if (selectedTracks.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[green]Selected {selectedTracks.Count} track(s):[/]");
            foreach (var track in selectedTracks)
            {
                AnsiConsole.MarkupLine($"  [dim]•[/] Track {track.Id}: [white]{Markup.Escape(track.Name ?? "Unknown")}[/] [dim]({track.SizeInGB:F2} GB)[/]");
            }
            AnsiConsole.WriteLine();
        }

        _logger.LogInformation($"User selected {selectedTracks.Count} tracks");
        return selectedTracks;
    }

    public Dictionary<AkTitle, EpisodeInfo> MapTracksToEpisodes(List<AkTitle> selectedTracks, string seriesTitle)
    {
        AnsiConsole.WriteLine();
        _promptService.DisplayHeader($"MANUAL MODE - Episode Mapping for {seriesTitle}");
        AnsiConsole.MarkupLine("[dim]Map each track to season and episode information:[/]");
        AnsiConsole.WriteLine();

        var trackMapping = new Dictionary<AkTitle, EpisodeInfo>();

        foreach (var track in selectedTracks)
        {
            var trackPanel = new Panel($"[white]{Markup.Escape(track.Name ?? "Unknown")}[/] [dim]({track.SizeInGB:F2} GB)[/]")
            {
                Header = new PanelHeader($"[cyan]Track {track.Id}[/]"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Cyan1)
            };
            AnsiConsole.Write(trackPanel);

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

            AnsiConsole.MarkupLine($"  [green]Mapped to[/] [yellow]S{season:D2}E{episode:D2}[/]");
            AnsiConsole.WriteLine();
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
        AnsiConsole.MarkupLine($"[green]Found movie:[/] [white]{Markup.Escape(movieResult.Title ?? "")}[/] [dim]({Markup.Escape(movieResult.Year ?? "")})[/]");
        AnsiConsole.WriteLine();

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
        AnsiConsole.MarkupLine($"[green]Found TV series:[/] [white]{Markup.Escape(seriesResult.Title ?? "")}[/] [dim]({Markup.Escape(seriesResult.Year ?? "")})[/]");
        AnsiConsole.WriteLine();

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
        
        AnsiConsole.MarkupLine($"[dim]Searching for {(isMovie ? "movie" : "TV series")}:[/] [white]'{Markup.Escape(titleResult.Value)}'[/]{(year.HasValue ? $" [dim]({year})[/]" : "")}");

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
                var resultPanel = new Panel(
                    new Markup($"[white]{Markup.Escape(result.Title ?? "")}[/] [dim]({Markup.Escape(result.Year ?? "")})[/]\n\n" +
                              $"[dim]{Markup.Escape(result.Plot ?? "")}[/]"))
                {
                    Header = new PanelHeader("[green]Found[/]"),
                    Border = BoxBorder.Rounded,
                    BorderStyle = new Style(Color.Green)
                };
                AnsiConsole.Write(resultPanel);

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
                AnsiConsole.MarkupLine("[yellow]No results found for that search.[/]");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during manual search");
            AnsiConsole.MarkupLine($"[red]Error during search:[/] {Markup.Escape(ex.Message)}");
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