using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using AutoMk.Interfaces;
using AutoMk.Models;
using AutoMk.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;


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

            Console.WriteLine("..enter Ctrl+C or Ctrl+Break to exit..");

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
                            Console.WriteLine($"Running in {(rip.ManualMode ? "MANUAL" : "AUTOMATIC")} mode.");
                            Console.WriteLine();
                            break;
                        case ModeSelectionSetting.AlwaysManual:
                            rip.ManualMode = true;
                            Console.WriteLine("Running in MANUAL mode (configured setting).");
                            Console.WriteLine();
                            break;
                        case ModeSelectionSetting.AlwaysAutomatic:
                            rip.ManualMode = false;
                            Console.WriteLine("Running in AUTOMATIC mode (configured setting).");
                            Console.WriteLine();
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
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(@"
    ╔═══════════════════════════════════════════════════════════════════╗
    ║                                                                   ║
    ║      █████╗ ██╗   ██╗████████╗ ██████╗       ███╗   ███╗██╗  ██╗  ║
    ║     ██╔══██╗██║   ██║╚══██╔══╝██╔═══██╗      ████╗ ████║██║ ██╔╝  ║
    ║     ███████║██║   ██║   ██║   ██║   ██║█████╗██╔████╔██║█████╔╝   ║
    ║     ██╔══██║██║   ██║   ██║   ██║   ██║╚════╝██║╚██╔╝██║██╔═██╗   ║
    ║     ██║  ██║╚██████╔╝   ██║   ╚██████╔╝      ██║ ╚═╝ ██║██║  ██╗  ║
    ║     ╚═╝  ╚═╝ ╚═════╝    ╚═╝    ╚═════╝       ╚═╝     ╚═╝╚═╝  ╚═╝  ║
    ║                                                                   ║
    ║              Automated MakeMKV Disc Ripping & Organization        ║
    ║                                                                   ║
    ║   • Continuous disc monitoring                                    ║
    ║   • Intelligent media identification (OMDB)                       ║
    ║   • Plex-compatible naming & organization                         ║
    ║   • Multi-disc TV series support                                  ║
    ║   • Interactive manual identification                             ║
    ║                                                                   ║
    ╚═══════════════════════════════════════════════════════════════════╝");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("                           Starting AutoMk...\n");
            Console.ResetColor();
        }

        private static bool PromptForModeSelection()
        {
            Console.WriteLine();
            DisplayHeader("MODE SELECTION");
            Console.WriteLine("Choose how AutoMk should process discs:");
            Console.WriteLine();
            Console.WriteLine("1. AUTOMATIC MODE (Default)");
            Console.WriteLine("   • Uses OMDB API to automatically identify media");
            Console.WriteLine("   • Uses state file to track TV series episodes");
            Console.WriteLine("   • Filters tracks by size settings");
            Console.WriteLine("   • Minimal user interaction required");
            Console.WriteLine();
            Console.WriteLine("2. MANUAL MODE");
            Console.WriteLine("   • User confirms all media identification");
            Console.WriteLine("   • User selects which tracks to rip");
            Console.WriteLine("   • User manually maps episodes for TV series");
            Console.WriteLine("   • State file is bypassed - fresh processing every time");
            Console.WriteLine();
            Console.WriteLine("3. CONFIGURE TV SERIES PROFILES");
            Console.WriteLine("   • Pre-configure settings for TV series");
            Console.WriteLine("   • Set episode sizes, sorting, and handling preferences");
            Console.WriteLine("   • View and edit existing series profiles");
            Console.WriteLine();

            while (true)
            {
                Console.Write("Enter your choice (1 for Automatic, 2 for Manual, 3 for Configure): ");
                var choice = Console.ReadLine()?.Trim();

                switch (choice)
                {
                    case "1":
                    case "":  // Default to automatic
                        return false;

                    case "3":
                        ConfigureSeriesProfiles();
                        // After configuration, ask again for mode
                        Console.WriteLine("Returning to mode selection...");
                        Console.WriteLine();
                        continue;

                    case "2":
                        return true;

                    default:
                        Console.WriteLine("Invalid choice. Please enter 1 or 2.");
                        Console.WriteLine();
                        break;
                }
            }
        }

        private static void ConfigureSeriesProfiles()
        {
            // Create a temporary service provider for the profile configuration
            var services = new ServiceCollection();

            services.AddSingleton<SeriesProfileService>();

            var serviceProvider = services.BuildServiceProvider();
            var profileService = serviceProvider.GetRequiredService<SeriesProfileService>();

            while (true)
            {
                Console.Clear();
                DisplayHeader("TV SERIES PROFILE CONFIGURATION");

                var profiles = profileService.GetAllProfilesAsync().GetAwaiter().GetResult();

                if (profiles.Count > 0)
                {
                    Console.WriteLine("Existing Series Profiles:");
                    Console.WriteLine();

                    for (int i = 0; i < profiles.Count; i++)
                    {
                        var profile = profiles[i];
                        Console.WriteLine($"{i + 1}. {profile.SeriesTitle}");
                        Console.WriteLine($"   Episode Size: {profile.MinEpisodeSizeGB ?? 0:F1} - {profile.MaxEpisodeSizeGB ?? 99:F1} GB");
                        Console.WriteLine($"   Sorting: {profile.TrackSortingStrategy}");
                        Console.WriteLine($"   Double Episodes: {profile.DoubleEpisodeHandling}");
                        Console.WriteLine($"   Auto-increment: {(profile.UseAutoIncrement ? "Enabled" : "Disabled")}");
                        Console.WriteLine();
                    }

                    Console.WriteLine($"{profiles.Count + 1}. Create new profile");
                    Console.WriteLine($"{profiles.Count + 2}. Return to mode selection");
                    Console.WriteLine();

                    Console.Write($"Enter your choice (1-{profiles.Count + 2}): ");
                }
                else
                {
                    Console.WriteLine("No series profiles configured yet.");
                    Console.WriteLine();
                    Console.WriteLine("1. Create new profile");
                    Console.WriteLine("2. Return to mode selection");
                    Console.WriteLine();

                    Console.Write("Enter your choice (1-2): ");
                }

                var choice = Console.ReadLine()?.Trim();

                if (profiles.Count > 0)
                {
                    if (int.TryParse(choice, out var index))
                    {
                        if (index >= 1 && index <= profiles.Count)
                        {
                            // Edit existing profile
                            EditSeriesProfile(profileService, profiles[index - 1]);
                        }
                        else if (index == profiles.Count + 1)
                        {
                            // Create new profile
                            CreateNewSeriesProfile(profileService);
                        }
                        else if (index == profiles.Count + 2)
                        {
                            // Return to mode selection
                            return;
                        }
                    }
                }
                else
                {
                    if (choice == "1")
                    {
                        CreateNewSeriesProfile(profileService);
                    }
                    else if (choice == "2")
                    {
                        return;
                    }
                }
            }
        }

        private static void CreateNewSeriesProfile(SeriesProfileService profileService)
        {
            Console.Clear();
            DisplayHeader("CREATE NEW SERIES PROFILE");

            Console.Write("Enter series title: ");
            var title = Console.ReadLine()?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(title))
            {
                Console.WriteLine("Series title cannot be empty. Press any key to continue...");
                Console.ReadKey();
                return;
            }

            Console.Write("Enter IMDb ID (optional, e.g., tt0106004): ");
            var imdbId = Console.ReadLine()?.Trim() ?? "";

            // Use a mock console interaction service for the profile creation
            var mockLogger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<ConsoleInteractionService>();
            var mockPromptService = new ConsolePromptService();
            var consoleService = new ConsoleInteractionService(null!, mockLogger, mockPromptService);

            var profile = consoleService.PromptForCompleteSeriesProfile(title, "Manual Configuration");

            profileService.CreateOrUpdateProfileAsync(profile).GetAwaiter().GetResult();

            Console.WriteLine();
            Console.WriteLine("Profile created successfully! Press any key to continue...");
            Console.ReadKey();
        }

        private static void EditSeriesProfile(SeriesProfileService profileService, SeriesProfile profile)
        {
            Console.Clear();
            DisplayHeader($"EDIT PROFILE: {profile.SeriesTitle}");

            Console.WriteLine("1. Delete this profile");
            Console.WriteLine("2. Edit settings");
            Console.WriteLine("3. Return to profile list");
            Console.WriteLine();

            Console.Write("Enter your choice (1-3): ");
            var choice = Console.ReadLine()?.Trim();

            switch (choice)
            {
                case "1":
                    Console.Write("Are you sure you want to delete this profile? (y/n): ");
                    if (Console.ReadLine()?.Trim().ToLowerInvariant() == "y")
                    {
                        profileService.DeleteProfileAsync(profile.SeriesTitle).GetAwaiter().GetResult();
                        Console.WriteLine("Profile deleted. Press any key to continue...");
                        Console.ReadKey();
                    }
                    break;

                case "2":
                    // Re-run the profile creation process to update settings
                    var mockLogger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<ConsoleInteractionService>();
                    var mockPromptService = new ConsolePromptService();
                    var consoleService = new ConsoleInteractionService(null!, mockLogger, mockPromptService);

                    var updatedProfile = consoleService.PromptForCompleteSeriesProfile(
                        profile.SeriesTitle,
                        "Edit Configuration");

                    profileService.CreateOrUpdateProfileAsync(updatedProfile).GetAwaiter().GetResult();

                    Console.WriteLine();
                    Console.WriteLine("Profile updated successfully! Press any key to continue...");
                    Console.ReadKey();
                    break;
            }
        }

        private static async Task CleanupExistingProcesses()
        {
            try
            {
                var processes = Process.GetProcessesByName("makemkvcon64");
                if (processes.Length > 0)
                {
                    Console.WriteLine($"Found {processes.Length} existing HandBrakeCLI process(es). Cleaning up...");

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
                            Console.WriteLine($"Warning: Could not kill HandBrakeCLI process {process.Id}: {ex.Message}");
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
                Console.WriteLine($"Error during process cleanup: {ex.Message}");
            }
        }

        private static void DisplayHeader(string title)
        {
            var border = new string('=', title.Length + 4);
            Console.WriteLine(border);
            Console.WriteLine($"  {title}  ");
            Console.WriteLine(border);
            Console.WriteLine();
        }
    }
}
