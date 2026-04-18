using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace MakeMkvEmulator;

class Program
{
    private static EmulatorConfiguration? _config;
    private static string _scenariosDir = Path.Combine(AppContext.BaseDirectory, "scenarios");
    private static string _statePath = Path.Combine(AppContext.BaseDirectory, "emulator-state.json");
    private static bool _bindAny;
    // Default keeps localhost transfers slow enough to watch the client's progress bar for
    // the emulator's ~1MB dummy output (~2s at 0.5 MB/s). Override with --throttle-mbps.
    private static double _throttleMBps = 0.5;
    private static bool _resetState;

    static int Main(string[] args)
    {
        try
        {
            // The `receive` command runs a long-lived HTTP listener that mimics AutoMk's file
            // transfer target. It doesn't need the scenario configuration, so handle it before
            // loading scenarios (which would otherwise fail if none are provisioned).
            var command = ParseCommand(args);
            LogInvocation(args, command);

            if (command.Command == "receive")
            {
                return RunReceiveServer(command.Port ?? 5000);
            }

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

    /// <summary>
    /// Logs what the emulator was asked to do so AutoMk's log panel has a record of every
    /// makemkvcon64 invocation (drive scans, title rips) and the receive server's boot
    /// parameters. CLI-mode calls go to stderr tagged [EMULATOR] so AutoMk treats them as
    /// informational; receive-mode writes to stderr for visibility in its own console.
    /// </summary>
    private static void LogInvocation(string[] args, CommandInfo command)
    {
        var rawArgs = args.Length == 0 ? "(none)" : string.Join(' ', args);
        var summary = command switch
        {
            { Command: "info", DiscId: null } => "list drives",
            { Command: "info", DiscId: not null } => $"disc info disc:{command.DiscId}",
            { Command: "mkv", DiscId: not null, TitleId: not null, OutputPath: not null }
                => $"rip title disc:{command.DiscId} title:{command.TitleId} → {command.OutputPath}",
            { Command: "receive" } => $"receive server port={command.Port ?? 5000}"
                + (_resetState ? " --reset-state" : "")
                + (_bindAny ? " --bind-any" : "")
                + (_throttleMBps > 0 ? $" throttle={_throttleMBps:F2}MB/s" : ""),
            _ => "(unknown command)"
        };

        Console.Error.WriteLine($"[EMULATOR] invoke: {summary}");
        Console.Error.WriteLine($"[EMULATOR] raw args: {rawArgs}");
    }

    private static bool LoadConfiguration()
    {
        try
        {
            if (!Directory.Exists(_scenariosDir))
            {
                Console.Error.WriteLine($"Scenarios directory not found: {_scenariosDir}");
                return false;
            }

            // Scenarios run in filename order — prefix files with 01-, 02-, etc. to control sequence.
            var scenarioFiles = Directory.GetFiles(_scenariosDir, "*.json")
                .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (scenarioFiles.Length == 0)
            {
                Console.Error.WriteLine($"No scenario files found in: {_scenariosDir}");
                return false;
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };

            var merged = new EmulatorConfiguration();
            EmulatedDrive? firstDrive = null;

            foreach (var file in scenarioFiles)
            {
                var json = File.ReadAllText(file);
                var scenario = JsonSerializer.Deserialize<EmulatorConfiguration>(json, options);
                if (scenario == null)
                {
                    Console.Error.WriteLine($"[EMULATOR] Skipping unparseable scenario: {Path.GetFileName(file)}");
                    continue;
                }

                merged.Discs.AddRange(scenario.Discs);
                firstDrive ??= scenario.Drive;
            }

            if (merged.Discs.Count == 0)
            {
                Console.Error.WriteLine("No discs loaded from any scenario file");
                return false;
            }

            if (firstDrive != null)
                merged.Drive = firstDrive;

            _config = merged;

            // Restore disc position and rotation flag across runs. Preserve an index past the
            // end of the scenario list — that's the "all scenarios processed, no disc" state
            // AdvanceToNextDisc parks on. Negative values are the only ones we clamp.
            if (File.Exists(_statePath))
            {
                var stateJson = File.ReadAllText(_statePath);
                var state = JsonSerializer.Deserialize<EmulatorState>(stateJson);
                if (state != null)
                {
                    _config.CurrentDiscIndex = Math.Max(0, state.CurrentDiscIndex);
                    _config.RotatePending = state.RotatePending;
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
            var state = new EmulatorState
            {
                CurrentDiscIndex = _config.CurrentDiscIndex,
                RotatePending = _config.RotatePending
            };
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
            else if (arg == "receive")
            {
                info.Command = "receive";
            }
            else if (arg == "--port" && i + 1 < args.Length && int.TryParse(args[i + 1], out var portValue))
            {
                info.Port = portValue;
                i++;
            }
            else if (arg.StartsWith("--port=") && int.TryParse(arg.Substring("--port=".Length), out var inlinePort))
            {
                info.Port = inlinePort;
            }
            else if (arg == "--bind-any")
            {
                _bindAny = true;
            }
            else if (arg == "--reset-state")
            {
                _resetState = true;
            }
            else if (arg == "--throttle-mbps" && i + 1 < args.Length &&
                double.TryParse(args[i + 1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var throttleValue))
            {
                _throttleMBps = throttleValue;
                i++;
            }
            else if (arg.StartsWith("--throttle-mbps=") &&
                double.TryParse(arg.Substring("--throttle-mbps=".Length),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var inlineThrottle))
            {
                _throttleMBps = inlineThrottle;
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

    /// <summary>
    /// Runs a lightweight HTTP server that mimics AutoMk's file transfer target.
    /// `GET /health` returns 200 so the client's availability check passes; `POST /upload`
    /// consumes the multipart payload and discards the file so the emulator never fills disk.
    /// </summary>
    private static int RunReceiveServer(int port)
    {
        // Opt-in reset. Without --reset-state we preserve the emulator's disc position
        // across restarts so the user can resume wherever they left off. This runs BEFORE
        // anything else so a fresh scenario replay is guaranteed by the time CLI-mode
        // `info` calls land.
        if (_resetState && File.Exists(_statePath))
        {
            try
            {
                File.Delete(_statePath);
                Console.Error.WriteLine($"[EMULATOR] --reset-state: disc position cleared ({_statePath})");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[EMULATOR] Failed to reset state: {ex.Message}");
            }
        }

        var builder = WebApplication.CreateBuilder();

        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "HH:mm:ss ";
        });
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        // Uploads can be multi-GB (rip output). Remove Kestrel's default 30MB body cap and the
        // form multipart cap so AutoMk can stream the full file through.
        builder.Services.Configure<KestrelServerOptions>(o =>
        {
            o.Limits.MaxRequestBodySize = null;
        });

        builder.Services.Configure<FormOptions>(o =>
        {
            o.MultipartBodyLengthLimit = long.MaxValue;
            o.ValueLengthLimit = int.MaxValue;
            o.MultipartHeadersLengthLimit = int.MaxValue;
        });

        var app = builder.Build();
        app.Urls.Clear();
        // Bind loopback only — this is a dev-time fake of AutoMk's transfer target, not a
        // service meant to be reachable from other hosts. Pass `--bind-any` to change that.
        var bindHost = _bindAny ? "0.0.0.0" : "127.0.0.1";
        app.Urls.Add($"http://{bindHost}:{port}");
        var logger = app.Services.GetRequiredService<ILogger<Program>>();

        app.MapGet("/health", () => Results.Ok(new { status = "healthy", role = "makemkv-emulator" }));

        app.MapPost("/upload", async (HttpRequest request, CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "expected multipart/form-data" });
            }

            var boundary = HeaderUtilities.RemoveQuotes(
                MediaTypeHeaderValue.Parse(request.ContentType).Boundary).Value;
            if (string.IsNullOrEmpty(boundary))
            {
                return Results.BadRequest(new { error = "missing multipart boundary" });
            }

            var reader = new MultipartReader(boundary, request.Body);
            string? metadata = null;
            string? receivedFileName = null;
            long bytesWritten = 0;
            string? tempPath = null;

            try
            {
                MultipartSection? section;
                while ((section = await reader.ReadNextSectionAsync(ct)) != null)
                {
                    var disposition = section.GetContentDispositionHeader();
                    if (disposition == null) continue;

                    if (disposition.IsFormDisposition())
                    {
                        var name = disposition.Name.Value?.Trim('"');
                        if (name == "metadata")
                        {
                            using var sr = new StreamReader(section.Body);
                            metadata = await sr.ReadToEndAsync(ct);
                        }
                    }
                    else if (disposition.IsFileDisposition())
                    {
                        receivedFileName = Path.GetFileName(
                            (disposition.FileName.Value ?? disposition.FileNameStar.Value ?? "upload").Trim('"'));
                        tempPath = Path.Combine(Path.GetTempPath(),
                            $"makemkv-emulator_{Guid.NewGuid():N}_{receivedFileName}");

                        await using var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write,
                            FileShare.None, bufferSize: 64 * 1024, useAsync: true);

                        bytesWritten = await CopyWithThrottleAsync(section.Body, fs, _throttleMBps, ct);
                    }
                }

                if (receivedFileName == null)
                {
                    return Results.BadRequest(new { error = "missing file part" });
                }

                logger.LogInformation("[EMULATOR] Received {FileName} ({Bytes} bytes) → {TempPath}",
                    receivedFileName, bytesWritten, tempPath);
                if (!string.IsNullOrEmpty(metadata))
                {
                    logger.LogDebug("[EMULATOR] metadata={Metadata}", metadata);
                }
            }
            finally
            {
                // Per the emulator's charter, always drop the received file after recording it.
                try
                {
                    if (tempPath != null && File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                        logger.LogInformation("[EMULATOR] Deleted received file: {TempPath}", tempPath);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[EMULATOR] Failed to delete received file: {TempPath}", tempPath);
                }
            }

            return Results.Ok(new
            {
                status = "received",
                fileName = receivedFileName,
                sizeBytes = bytesWritten,
            });
        });

        Console.Error.WriteLine($"[EMULATOR] Receive server listening on http://{bindHost}:{port}");
        Console.Error.WriteLine("[EMULATOR]   GET  /health");
        Console.Error.WriteLine("[EMULATOR]   POST /upload  (multipart/form-data; file discarded after receipt)");
        if (_throttleMBps > 0)
        {
            Console.Error.WriteLine($"[EMULATOR]   Throttle: {_throttleMBps:F2} MB/s (server-side read pacing)");
        }

        app.Run();
        return 0;
    }

    /// <summary>
    /// Copies <paramref name="source"/> to <paramref name="destination"/>, pacing reads so the
    /// observed rate does not exceed <paramref name="throttleMBps"/>. The slow reads create TCP
    /// back-pressure, which makes the client's upload progress bar actually advance over time
    /// instead of snapping to 100% because loopback buffers swallowed the whole body. When
    /// <paramref name="throttleMBps"/> is &lt;= 0 this falls back to a straight copy.
    /// </summary>
    private static async Task<long> CopyWithThrottleAsync(Stream source, Stream destination,
        double throttleMBps, CancellationToken ct)
    {
        if (throttleMBps <= 0)
        {
            await source.CopyToAsync(destination, ct);
            return destination.CanSeek ? destination.Length : 0;
        }

        // Small chunks keep the pacing granular — the client sees the rate update frequently
        // rather than stepping up once per big chunk.
        const int chunkSize = 16 * 1024;
        var bytesPerSecond = throttleMBps * 1024.0 * 1024.0;
        var buffer = new byte[chunkSize];
        var stopwatch = Stopwatch.StartNew();
        long total = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, chunkSize), ct);
            if (read == 0) break;

            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            total += read;

            var expectedSeconds = total / bytesPerSecond;
            var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
            var delaySeconds = expectedSeconds - elapsedSeconds;
            if (delaySeconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
            }
        }

        return total;
    }

    private static int ListDrives()
    {
        // If a rip happened since the last info query, rotate to the next disc before reporting.
        if (_config != null && _config.RotatePending)
        {
            _config.RotatePending = false;
            AdvanceToNextDisc();
        }

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
        Console.WriteLine($"MSG:3023,0,0,\"Analyzing disc structure\"");
        Console.WriteLine($"MSG:3024,0,0,\"Detected {disc.Titles.Count} titles on source media\"");
        Console.WriteLine($"MSG:3025,0,0,\"Opening source files for title {title.Name}\"");
        Console.WriteLine($"MSG:5003,0,0,\"Saving {disc.Titles.Count} titles into directory {outputPath}\"");
        Console.WriteLine($"MSG:4001,0,1,\"Saving title {title.Name}\"");
        Console.WriteLine($"MSG:4017,0,0,\"Audio track 1: DTS-HD MA 5.1 English\"");
        Console.WriteLine($"MSG:4017,0,0,\"Audio track 2: AC3 2.0 Commentary\"");
        Console.WriteLine($"MSG:4018,0,0,\"Subtitle track 1: English (PGS)\"");
        Console.WriteLine($"MSG:4018,0,0,\"Subtitle track 2: Spanish (PGS)\"");

        // Phase messages emitted at fixed progress percentages. Gives the log panel enough
        // chatter to scroll while progress bars remain pinned below.
        var phaseMessages = new (int Percent, string Message)[]
        {
            (5,  "MSG:3028,0,0,\"Scanning CD-ROM devices for {0}\""),
            (15, "MSG:3031,0,0,\"Analyzing seamless segments\""),
            (25, "MSG:3307,0,0,\"Starting AV reconstruction for {0}\""),
            (35, "MSG:4019,0,0,\"Muxing audio stream 1 of 2\""),
            (45, "MSG:4020,0,0,\"Muxing audio stream 2 of 2\""),
            (55, "MSG:4021,0,0,\"Processing subtitle segments\""),
            (65, "MSG:3033,0,0,\"Validating title integrity\""),
            (75, "MSG:3040,0,0,\"Writing output segments to disk\""),
            (85, "MSG:3041,0,0,\"Finalizing container metadata\""),
            (95, "MSG:3042,0,0,\"Verifying checksums\""),
        };

        // Simulate ripping with progress updates
        var duration = title.RipDurationSeconds;
        var steps = 100;
        var delay = (duration * 1000) / steps;
        var nextPhaseIdx = 0;

        for (int i = 0; i <= steps; i++)
        {
            var percentage = i;
            var currentFps = 15.0 + (i % 10); // Simulate varying FPS
            var avgFps = 18.5;
            var remaining = ((steps - i) * delay) / 1000;

            // Emit phase message when we cross its threshold
            while (nextPhaseIdx < phaseMessages.Length && i >= phaseMessages[nextPhaseIdx].Percent)
            {
                var msg = phaseMessages[nextPhaseIdx].Message.Replace("{0}", title.Name);
                Console.WriteLine(msg);
                nextPhaseIdx++;
            }

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

            // Mark the disc as "done from the emulator's point of view" — the next info query
            // will rotate to the next disc regardless of how many titles the caller ripped.
            _config.RotatePending = true;
            SaveState();

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

        if (_config.CurrentDiscIndex >= _config.Discs.Count)
        {
            // Wrap so the scenario list loops endlessly — the emulator is a test harness, not
            // a real session, and wrapping lets AutoMk keep exercising the full flow without
            // the user having to reset state between runs.
            _config.CurrentDiscIndex = 0;
            Console.Error.WriteLine($"[EMULATOR] Completed scenario cycle. Looping back to disc 1/{_config.Discs.Count}: {_config.CurrentDisc?.Name}");
        }
        else
        {
            Console.Error.WriteLine($"[EMULATOR] Advanced to disc {_config.CurrentDiscIndex + 1}/{_config.Discs.Count}: {_config.CurrentDisc?.Name}");
        }

        SaveState();
    }

    private static int ShowUsage()
    {
        Console.WriteLine("MakeMKV Emulator - Usage:");
        Console.WriteLine("  emulator -r --progress=-same info");
        Console.WriteLine("  emulator -r --progress=-same info disc:0");
        Console.WriteLine("  emulator -r --progress=-same mkv disc:0 0 \"output/path\"");
        Console.WriteLine("  emulator receive [--port 5000] [--bind-any] [--throttle-mbps 0.5] [--reset-state]   # HTTP endpoint mimicking AutoMk's transfer target; --reset-state wipes disc position before starting");
        return 1;
    }

    private class CommandInfo
    {
        public string? Command { get; set; }
        public string? DiscId { get; set; }
        public string? TitleId { get; set; }
        public string? OutputPath { get; set; }
        public int? Port { get; set; }
    }

    private class EmulatorState
    {
        public int CurrentDiscIndex { get; set; }

        /// <summary>
        /// Set to true after any RipTitle call, cleared by the next ListDrives (info) call which
        /// also advances CurrentDiscIndex. This is how the emulator rotates through scenarios
        /// regardless of which title(s) the caller chose to rip.
        /// </summary>
        public bool RotatePending { get; set; }
    }
}
