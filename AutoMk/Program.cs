using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutoMk.Interfaces;
using AutoMk.Models;
using AutoMk.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console;


namespace AutoMk
{
    class Program
    {

        static async Task Main(string[] args)
        {
            // Display startup banner
            DisplayStartupBanner();

            // Clean up any existing HandBrakeCLI processes
            await CleanupExistingProcesses();

            var config = Configure();

            AnsiConsole.MarkupLine("[dim]..enter Ctrl+C or Ctrl+Break to exit..[/]");

            // But for the host, use Host.CreateDefaultBuilder
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((context, builder) =>
                {
                    // Configuration is automatically set up, but you can customize it
                    builder.AddConfiguration(config);
                })
                .ConfigureServices((context, services) =>
                {
                    var omdbSettings = config.GetSection("OmdbSettings").Get<OmdbSettings>()
                        ?? throw new InvalidOperationException("OmdbSettings configuration is required");
                    var rip = config.GetSection("RipSettings").Get<RipSettings>()
                        ?? throw new InvalidOperationException("RipSettings configuration is required");

                    // Handle mode selection based on setting
                    switch (rip.ModeSelection)
                    {
                        case ModeSelectionSetting.Ask:
                            rip.ManualMode = PromptForModeSelection();
                            AnsiConsole.MarkupLine($"[green]Running in[/] [yellow]{(rip.ManualMode ? "MANUAL" : "AUTOMATIC")}[/] [green]mode.[/]");
                            AnsiConsole.WriteLine();
                            break;
                        case ModeSelectionSetting.AlwaysManual:
                            rip.ManualMode = true;
                            AnsiConsole.MarkupLine("[green]Running in[/] [yellow]MANUAL[/] [green]mode (configured setting).[/]");
                            AnsiConsole.WriteLine();
                            break;
                        case ModeSelectionSetting.AlwaysAutomatic:
                            rip.ManualMode = false;
                            AnsiConsole.MarkupLine("[green]Running in[/] [yellow]AUTOMATIC[/] [green]mode (configured setting).[/]");
                            AnsiConsole.WriteLine();
                            break;
                    }

                    services.AddSingleton(omdbSettings);
                    // Register HttpClients
                    services.AddHttpClient<OmdbClient>();
                    services.AddHttpClient<FileTransferClient>();

                    // Register settings
                    services.AddSingleton(rip);
                    if (rip.FileTransferSettings != null)
                    {
                        services.AddSingleton(rip.FileTransferSettings);
                    }
                    else
                    {
                        services.AddSingleton(new FileTransferSettings());
                    }

                    // Register legacy services
                    services.AddSingleton<DriveWatcher>();

                    // Register MakeMKV services
                    services.AddSingleton<MakeMkvProcessManager>();
                    services.AddSingleton<MakeMkvOutputParser>();
                    services.AddSingleton<MakeMkvProgressReporter>();
                    services.AddSingleton<IMakeMkvService, MakeMkvService>();

                    // Register media services with interfaces
                    services.AddSingleton<IMediaNamingService, MediaNamingService>();
                    services.AddSingleton<IMediaStateManager, MediaStateManager>();
                    services.AddSingleton<IOmdbClient, OmdbClient>();
                    services.AddSingleton<IFileTransferClient, FileTransferClient>();

                    // Register console prompt service first
                    services.AddSingleton<IConsolePromptService, ConsolePromptService>();

                    // Register console output service
                    services.AddSingleton<IConsoleOutputService, ConsoleOutputService>();

                    // Register progress manager for concurrent progress bars
                    services.AddSingleton(new ProgressManagerOptions());
                    services.AddSingleton<IProgressManager, ProgressManager>();

                    services.AddSingleton<ConsoleInteractionService>();
                    services.AddSingleton<ManualModeService>();
                    services.AddSingleton<IMediaIdentificationService, MediaIdentificationService>();
                    services.AddSingleton<IMediaMoverService, MediaMoverService>();

                    // Register new services for improved user interaction
                    services.AddSingleton<ISeriesProfileService, SeriesProfileService>();

                    // Register season caching services
                    services.AddSingleton<ISeasonInfoCacheService, SeasonInfoCacheService>();
                    services.AddSingleton<IEnhancedOmdbService, EnhancedOmdbService>();

                    // Register file discovery and media selection services
                    services.AddSingleton<IFileDiscoveryService, FileDiscoveryService>();
                    services.AddSingleton<IMediaSelectionService, MediaSelectionService>();
                    services.AddSingleton<ISeriesConfigurationService, SeriesConfigurationService>();

                    // Register pattern learning service
                    services.AddSingleton<IPatternLearningService, PatternLearningService>();

                    // Register discover and name service
                    services.AddSingleton<IDiscoverAndNameService, DiscoverAndNameService>();

                    services.AddLogging((loggingBuilder) =>
                    {
                        // Clear default providers first (Host.CreateDefaultBuilder adds console by default)
                        loggingBuilder.ClearProviders();
                        
                        loggingBuilder.AddConfiguration(config.GetSection("Logging"));
                        loggingBuilder.SetMinimumLevel(LogLevel.Trace);

                        // Only add console logging if enabled in settings
                        if (rip.ShowConsoleLogging)
                        {
                            loggingBuilder.AddConsole();
                        }

                        loggingBuilder.AddFile(o => o.RootPath = AppContext.BaseDirectory);
                    });
                    // Register the background monitoring service
                    services.AddHostedService<MakeMkAuto>();


                    // Access configuration like this:
                    // var connectionString = context.Configuration.GetConnectionString("Default");
                })
                .Build(); // This returns IHost

            await host.RunAsync();
        }


        private static IConfigurationRoot Configure()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables();


#if DEBUG
            builder = builder.AddUserSecrets<Program>();
#endif

            var configuration = builder.Build();



            return configuration;

        }


        private static void DisplayStartupBanner()
        {
            var banner = new Panel(
                new Markup(@"[cyan]
  █████╗ ██╗   ██╗████████╗ ██████╗       ███╗   ███╗██╗  ██╗
 ██╔══██╗██║   ██║╚══██╔══╝██╔═══██╗      ████╗ ████║██║ ██╔╝
 ███████║██║   ██║   ██║   ██║   ██║█████╗██╔████╔██║█████╔╝
 ██╔══██║██║   ██║   ██║   ██║   ██║╚════╝██║╚██╔╝██║██╔═██╗
 ██║  ██║╚██████╔╝   ██║   ╚██████╔╝      ██║ ╚═╝ ██║██║  ██╗
 ╚═╝  ╚═╝ ╚═════╝    ╚═╝    ╚═════╝       ╚═╝     ╚═╝╚═╝  ╚═╝[/]

        [white]Automated MakeMKV Disc Ripping & Organization[/]

 [dim]•[/] Continuous disc monitoring
 [dim]•[/] Intelligent media identification (OMDB)
 [dim]•[/] Plex-compatible naming & organization
 [dim]•[/] Multi-disc TV series support
 [dim]•[/] Interactive manual identification"))
            {
                Border = BoxBorder.Double,
                BorderStyle = new Style(Color.Cyan1),
                Padding = new Padding(2, 0, 2, 0)
            };

            AnsiConsole.Write(banner);
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]                        Starting AutoMk...[/]");
            AnsiConsole.WriteLine();
        }

        private static bool PromptForModeSelection()
        {
            while (true)
            {
                AnsiConsole.WriteLine();

                var rule = new Rule("[cyan]MODE SELECTION[/]")
                {
                    Justification = Justify.Center,
                    Style = Style.Parse("cyan")
                };
                AnsiConsole.Write(rule);
                AnsiConsole.WriteLine();

                AnsiConsole.MarkupLine("[dim]Use arrow keys to navigate, Enter to select[/]");
                AnsiConsole.WriteLine();

                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("[white]Choose how AutoMk should process discs:[/]")
                        .PageSize(10)
                        .HighlightStyle(new Style(Color.Cyan1))
                        .AddChoices(new[]
                        {
                            "Automatic Mode",
                            "Manual Mode",
                            "Discover and Name Mode"
                        })
                        .UseConverter(item => item switch
                        {
                            "Automatic Mode" => "[green]Automatic Mode[/] [dim]- Uses OMDB API, state tracking, size filtering. Minimal interaction.[/]",
                            "Manual Mode" => "[yellow]Manual Mode[/] [dim]- User confirms all identification, selects tracks, maps episodes.[/]",
                            "Discover and Name Mode" => "[magenta]Discover and Name Mode[/] [dim]- Find existing MKV files and organize them.[/]",
                            _ => item
                        }));

                switch (choice)
                {
                    case "Automatic Mode":
                        return false;

                    case "Manual Mode":
                        return true;

                    case "Discover and Name Mode":
                        RunDiscoverAndNameMode();
                        AnsiConsole.WriteLine();

                        var nextAction = AnsiConsole.Prompt(
                            new SelectionPrompt<string>()
                                .Title("[white]What would you like to do next?[/]")
                                .HighlightStyle(new Style(Color.Cyan1))
                                .AddChoices(new[]
                                {
                                    "Run Discover Mode Again",
                                    "Return to Mode Selection",
                                    "Quit"
                                }));

                        if (nextAction == "Run Discover Mode Again")
                        {
                            continue;
                        }
                        else if (nextAction == "Return to Mode Selection")
                        {
                            AnsiConsole.MarkupLine("[dim]Returning to mode selection...[/]");
                            continue;
                        }
                        else
                        {
                            AnsiConsole.MarkupLine("[dim]Exiting...[/]");
                            Environment.Exit(0);
                        }
                        break;
                }
            }
        }

        private static void RunDiscoverAndNameMode()
        {
            // Create a temporary service provider for the discover and name service
            var config = Configure();
            var omdbSettings = config.GetSection("OmdbSettings").Get<OmdbSettings>()
                ?? throw new InvalidOperationException("OmdbSettings configuration is required");
            var rip = config.GetSection("RipSettings").Get<RipSettings>()
                ?? throw new InvalidOperationException("RipSettings configuration is required");

            var services = new ServiceCollection();

            // Register HttpClients
            services.AddHttpClient<OmdbClient>();
            services.AddHttpClient<FileTransferClient>();

            // Register settings
            services.AddSingleton(omdbSettings);
            services.AddSingleton(rip);
            if (rip.FileTransferSettings != null)
            {
                services.AddSingleton(rip.FileTransferSettings);
            }
            else
            {
                services.AddSingleton(new FileTransferSettings());
            }

            // Register logging
            services.AddLogging(loggingBuilder =>
            {
                loggingBuilder.ClearProviders();
                loggingBuilder.SetMinimumLevel(LogLevel.Information);
                if (rip.ShowConsoleLogging)
                {
                    loggingBuilder.AddConsole();
                }
            });

            // Register all required services
            services.AddSingleton<IConsolePromptService, ConsolePromptService>();
            services.AddSingleton<IConsoleOutputService, ConsoleOutputService>();
            services.AddSingleton<IOmdbClient, OmdbClient>();
            services.AddSingleton<IFileTransferClient, FileTransferClient>();
            services.AddSingleton<IMediaNamingService, MediaNamingService>();
            services.AddSingleton<IMediaMoverService, MediaMoverService>();
            services.AddSingleton<ISeasonInfoCacheService, SeasonInfoCacheService>();
            services.AddSingleton<IEnhancedOmdbService, EnhancedOmdbService>();
            services.AddSingleton<IMediaSelectionService, MediaSelectionService>();
            services.AddSingleton<IDiscoverAndNameService, DiscoverAndNameService>();

            var serviceProvider = services.BuildServiceProvider();
            var discoverService = serviceProvider.GetRequiredService<IDiscoverAndNameService>();

            // Run the discover and name workflow
            discoverService.RunDiscoverAndNameWorkflowAsync().GetAwaiter().GetResult();
        }

        private static async Task CleanupExistingProcesses()
        {
            try
            {
                var processes = Process.GetProcessesByName("makemkvcon64");
                if (processes.Length > 0)
                {
                    AnsiConsole.MarkupLine($"[yellow]Found {processes.Length} existing makemkvcon64 process(es). Cleaning up...[/]");

                    foreach (var process in processes)
                    {
                        try
                        {
                            if (!process.HasExited)
                            {
                                process.Kill();
                                await process.WaitForExitAsync();
                            }
                        }
                        catch (Exception ex)
                        {
                            AnsiConsole.MarkupLine($"[yellow]Warning:[/] Could not kill makemkvcon64 process {process.Id}: {Markup.Escape(ex.Message)}");
                        }
                        finally
                        {
                            process.Dispose();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error during process cleanup:[/] {Markup.Escape(ex.Message)}");
            }
        }
    }
}
