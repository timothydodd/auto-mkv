using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace MakeMkvEmulator;

class Program
{
    private static EmulatorConfiguration? _config;
    private static string _configPath = "emulator-config.json";
    private static string _statePath = "emulator-state.json";

    static int Main(string[] args)
    {
        try
        {
            // Load configuration
            if (!LoadConfiguration())
            {
                Console.Error.WriteLine("Failed to load emulator configuration");
                return 1;
            }

            if (_config == null)
            {
                Console.Error.WriteLine("Configuration is null");
                return 1;
            }

            // Parse command line arguments
            var command = ParseCommand(args);

            return command switch
            {
                { Command: "info", DiscId: null } => ListDrives(),
                { Command: "info", DiscId: not null } => GetDiscInfo(command.DiscId),
                { Command: "mkv", DiscId: not null, TitleId: not null, OutputPath: not null }
                    => RipTitle(command.DiscId, command.TitleId, command.OutputPath),
                _ => ShowUsage()
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static bool LoadConfiguration()
    {
        try
        {
            // Load main configuration
            if (!File.Exists(_configPath))
            {
                Console.Error.WriteLine($"Configuration file not found: {_configPath}");
                return false;
            }

            var json = File.ReadAllText(_configPath);
            _config = JsonSerializer.Deserialize<EmulatorConfiguration>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            });

            if (_config == null)
            {
                Console.Error.WriteLine("Failed to deserialize configuration");
                return false;
            }

            // Load state (current disc index)
            if (File.Exists(_statePath))
            {
                var stateJson = File.ReadAllText(_statePath);
                var state = JsonSerializer.Deserialize<EmulatorState>(stateJson);
                if (state != null)
                {
                    _config.CurrentDiscIndex = state.CurrentDiscIndex;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error loading configuration: {ex.Message}");
            return false;
        }
    }

    private static void SaveState()
    {
        if (_config == null) return;

        try
        {
            var state = new EmulatorState { CurrentDiscIndex = _config.CurrentDiscIndex };
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_statePath, json);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error saving state: {ex.Message}");
        }
    }

    private static CommandInfo ParseCommand(string[] args)
    {
        var info = new CommandInfo();

        // Simple parsing - looking for key patterns
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg == "info")
            {
                info.Command = "info";
            }
            else if (arg == "mkv")
            {
                info.Command = "mkv";
            }
            else if (arg.StartsWith("disc:"))
            {
                info.DiscId = arg.Substring(5);
            }
            else if (info.Command == "mkv" && int.TryParse(arg, out int titleId))
            {
                info.TitleId = arg;
            }
            else if (info.Command == "mkv" && i == args.Length - 1)
            {
                // Last argument is output path
                info.OutputPath = arg.Trim('"');
            }
        }

        return info;
    }

    private static int ListDrives()
    {
        if (_config?.CurrentDisc == null)
        {
            Console.WriteLine("MSG:1005,0,1,\"No disc\"");
            Console.WriteLine("MSG:5010,0,0,\"Evaluation version\"");
            return 0;
        }

        var drive = _config.Drive;
        var disc = _config.CurrentDisc;

        // Mark disc as loaded
        disc.IsLoaded = true;

        // Output drive info
        // Format: DRV:id,visible,enabled,flags,driveName,discName,deviceName
        // Flags: 1=no disc, 12=disc with data, 28=disc ready to read
        Console.WriteLine($"DRV:{drive.Id},0,1,12,\"{drive.DriveName}\",\"{disc.Name}\",\"{drive.DriveLetter}\"");

        Console.WriteLine("MSG:5010,0,0,\"Evaluation version\"");
        return 0;
    }

    private static int GetDiscInfo(string discId)
    {
        if (_config?.CurrentDisc == null)
        {
            Console.WriteLine("MSG:1005,0,1,\"No disc\"");
            return 1;
        }

        var disc = _config.CurrentDisc;
        disc.IsLoaded = true;

        // Output disc scanning messages
        Console.WriteLine($"MSG:3307,0,0,\"Using direct disc access mode\"");
        Console.WriteLine($"MSG:5011,0,0,\"Operation successfully completed\"");

        // Output title information
        foreach (var title in disc.Titles)
        {
            // If source file name is provided, output MPLS addition message
            if (!string.IsNullOrEmpty(title.SourceFileName))
            {
                Console.WriteLine($"MSG:3028,0,3,\"File {title.SourceFileName} was added as title #{title.Id}\"");
            }

            // Output title properties
            // TINFO format: TINFO:titleId,propertyId,unknown,value
            Console.WriteLine($"TINFO:{title.Id},2,0,\"{title.Name}\"");
            Console.WriteLine($"TINFO:{title.Id},8,0,\"{title.ChapterCount}\"");
            Console.WriteLine($"TINFO:{title.Id},9,0,\"{title.Duration}\"");
            Console.WriteLine($"TINFO:{title.Id},10,0,\"{title.SizeString}\"");
            Console.WriteLine($"TINFO:{title.Id},27,0,\"{title.Name}\"");
        }

        Console.WriteLine($"MSG:5010,0,0,\"Evaluation version\"");
        return 0;
    }

    private static int RipTitle(string discId, string titleId, string outputPath)
    {
        if (_config?.CurrentDisc == null)
        {
            Console.WriteLine("MSG:1005,0,1,\"No disc\"");
            return 1;
        }

        var disc = _config.CurrentDisc;
        var title = disc.Titles.FirstOrDefault(t => t.Id == titleId);

        if (title == null)
        {
            Console.WriteLine($"MSG:3025,0,1,\"Title {titleId} is not available\"");
            return 1;
        }

        Console.WriteLine($"MSG:3307,0,0,\"Using direct disc access mode\"");
        Console.WriteLine($"MSG:5003,0,0,\"Saving {disc.Titles.Count} titles into directory {outputPath}\"");
        Console.WriteLine($"MSG:4001,0,1,\"Saving title {title.Name}\"");

        // Simulate ripping with progress updates
        var duration = title.RipDurationSeconds;
        var steps = 100;
        var delay = (duration * 1000) / steps;

        for (int i = 0; i <= steps; i++)
        {
            var percentage = i;
            var currentFps = 15.0 + (i % 10); // Simulate varying FPS
            var avgFps = 18.5;
            var remaining = ((steps - i) * delay) / 1000;

            // PRGV format: PRGV:id,currentBytes,totalBytes,currentSeconds,totalSeconds
            Console.WriteLine($"PRGV:0,{i},{steps},0,0");
            // PRGC format: progress current, max
            Console.WriteLine($"PRGC:0,\"{percentage}%\",\"{currentFps} fps, avg {avgFps} fps, {remaining}s remaining\"");

            Thread.Sleep(delay);
        }

        // Create output file
        var fileName = Path.GetFileName(title.Name);
        var fullPath = Path.Combine(outputPath, fileName);

        try
        {
            Directory.CreateDirectory(outputPath);

            // Create a dummy file with approximately the right size
            var sizeBytes = (long)(title.SizeGB * 1024 * 1024 * 1024);
            using (var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
            {
                // Write a small header to make it a valid file
                fs.SetLength(Math.Min(sizeBytes, 1024 * 1024)); // Cap at 1MB for emulation
            }

            Console.WriteLine($"MSG:5005,0,0,\"{disc.Titles.Count} titles saved\"");
            Console.WriteLine($"MSG:4002,0,1,\"{title.Name} saved to {fullPath}\"");
            Console.WriteLine($"MSG:5010,0,0,\"Evaluation version\"");

            // After successful rip of all titles on a disc, advance to next disc
            // (In real usage, AutoMk would rip all titles before ejecting)
            // We'll advance disc when the last title is ripped
            if (title.Id == disc.Titles.Last().Id)
            {
                AdvanceToNextDisc();
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error creating output file: {ex.Message}");
            Console.WriteLine($"MSG:3025,0,1,\"Failed to save title {titleId}\"");
            return 1;
        }
    }

    private static void AdvanceToNextDisc()
    {
        if (_config == null) return;

        _config.CurrentDiscIndex++;
        SaveState();

        if (_config.CurrentDiscIndex >= _config.Discs.Count)
        {
            Console.Error.WriteLine($"[EMULATOR] All discs processed. Resetting to first disc.");
            _config.CurrentDiscIndex = 0;
            SaveState();
        }
        else
        {
            Console.Error.WriteLine($"[EMULATOR] Advanced to disc {_config.CurrentDiscIndex + 1}/{_config.Discs.Count}: {_config.CurrentDisc?.Name}");
        }
    }

    private static int ShowUsage()
    {
        Console.WriteLine("MakeMKV Emulator - Usage:");
        Console.WriteLine("  emulator -r --progress=-same info");
        Console.WriteLine("  emulator -r --progress=-same info disc:0");
        Console.WriteLine("  emulator -r --progress=-same mkv disc:0 0 \"output/path\"");
        return 1;
    }

    private class CommandInfo
    {
        public string? Command { get; set; }
        public string? DiscId { get; set; }
        public string? TitleId { get; set; }
        public string? OutputPath { get; set; }
    }

    private class EmulatorState
    {
        public int CurrentDiscIndex { get; set; }
    }
}
