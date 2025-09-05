using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMk.Interfaces;
using AutoMk.Models;
using AutoMk.Utilities;
using Microsoft.Extensions.Logging;

namespace AutoMk.Services;

public class MakeMkvService : IMakeMkvService
{
    private readonly MakeMkvProcessManager _processManager;
    private readonly MakeMkvOutputParser _outputParser;
    private readonly MakeMkvProgressReporter _progressReporter;
    private readonly ILogger<MakeMkvService> _logger;
    private readonly IConsoleOutputService _consoleOutput;

    public MakeMkvService(
        MakeMkvProcessManager processManager,
        MakeMkvOutputParser outputParser,
        MakeMkvProgressReporter progressReporter,
        ILogger<MakeMkvService> logger,
        IConsoleOutputService consoleOutput)
    {
        _processManager = ValidationHelper.ValidateNotNull(processManager);
        _outputParser = ValidationHelper.ValidateNotNull(outputParser);
        _progressReporter = ValidationHelper.ValidateNotNull(progressReporter);
        _logger = ValidationHelper.ValidateNotNull(logger);
        _consoleOutput = ValidationHelper.ValidateNotNull(consoleOutput);
    }

    public async Task<bool> GetDiscInfoAsync(AkDriveInfo drive)
    {
        _logger.LogInformation("Getting disc info for drive {DriveId}", drive.Id);

        var arguments = $"-r --progress=-same info disc:{drive.Id}";
        using var process = _processManager.CreateProcess(arguments);

        if (process == null)
        {
            _logger.LogError("Failed to create MakeMKV process");
            return false;
        }

        try
        {
            process.Start();

            while (!process.StandardOutput.EndOfStream)
            {
                var line = await process.StandardOutput.ReadLineAsync();
                if (!string.IsNullOrEmpty(line))
                {
                    _logger.LogDebug("MakeMKV output: {Line}", line);
                    _outputParser.ParseLine(line, drive);
                }
            }

            await process.WaitForExitAsync();

            _logger.LogInformation("Found {TitleCount} titles on disc {DriveId}", drive.Titles.Count, drive.Id);
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting disc information");
            return false;
        }
    }

    public List<AkTitle> FilterTitlesBySize(AkDriveInfo drive, double minSizeGB = 3.0, double maxSizeGB = 12.0)
    {
        var filteredTitles = new List<AkTitle>();

        foreach (var title in drive.Titles.Values)
        {
            // Use pre-parsed size
            if (title.SizeInGB > 0)
            {
                if (title.SizeInGB >= minSizeGB && title.SizeInGB <= maxSizeGB)
                {
                    filteredTitles.Add(title);
                    _logger.LogInformation("Title {TitleId}: {TitleName} ({Size:F2} GB) - Selected",
                        title.Id, title.Name, title.SizeInGB);
                }
                else
                {
                    _logger.LogDebug("Title {TitleId}: {TitleName} ({Size:F2} GB) - Filtered out (not in range {MinSize}-{MaxSize} GB)",
                        title.Id, title.Name, title.SizeInGB, minSizeGB, maxSizeGB);
                }
            }
            else
            {
                _logger.LogDebug("Title {TitleId}: {TitleName} ({SizeString}) - Filtered out (unable to parse size)",
                    title.Id, title.Name, title.Size);
            }
        }

        _logger.LogInformation("Filtered {FilteredCount} titles in size range {MinSize}-{MaxSize} GB from {TotalCount} total titles",
            filteredTitles.Count, minSizeGB, maxSizeGB, drive.Titles.Count);

        return filteredTitles;
    }

    public List<AkTitle> FilterTitlesBySizeForTvSeries(AkDriveInfo drive, double minSizeGB = 3.0)
    {
        var filteredTitles = new List<AkTitle>();

        // First, find all titles that meet the minimum size requirement
        var candidateTitles = drive.Titles.Values
            .Where(t => t.SizeInGB >= minSizeGB)
            .OrderBy(t => t.SizeInGB)
            .ToList();

        if (!candidateTitles.Any())
        {
            _logger.LogWarning("No titles found meeting minimum size requirement of {MinSize} GB", minSizeGB);
            return filteredTitles;
        }

        // Find the smallest track that meets the minimum requirement
        var smallestValidTrack = candidateTitles.First();
        
        // Calculate dynamic max size as 3x the smallest valid track
        var dynamicMaxSizeGB = smallestValidTrack.SizeInGB * 3.0;

        _logger.LogInformation("TV Series filtering: Smallest valid track is {SmallestSize:F2} GB, dynamic max size set to {MaxSize:F2} GB",
            smallestValidTrack.SizeInGB, dynamicMaxSizeGB);

        // Now filter using the dynamic max size
        foreach (var title in drive.Titles.Values)
        {
            // Use pre-parsed size
            if (title.SizeInGB > 0)
            {
                if (title.SizeInGB >= minSizeGB && title.SizeInGB <= dynamicMaxSizeGB)
                {
                    filteredTitles.Add(title);
                    _logger.LogInformation("Title {TitleId}: {TitleName} ({Size:F2} GB) - Selected (TV Series filtering)",
                        title.Id, title.Name, title.SizeInGB);
                }
                else
                {
                    _logger.LogDebug("Title {TitleId}: {TitleName} ({Size:F2} GB) - Filtered out (not in range {MinSize}-{MaxSize} GB)",
                        title.Id, title.Name, title.SizeInGB, minSizeGB, dynamicMaxSizeGB);
                }
            }
            else
            {
                _logger.LogDebug("Title {TitleId}: {TitleName} ({SizeString}) - Filtered out (unable to parse size)",
                    title.Id, title.Name, title.Size);
            }
        }

        _logger.LogInformation("TV Series filtering: Selected {FilteredCount} titles in dynamic size range {MinSize}-{MaxSize} GB from {TotalCount} total titles",
            filteredTitles.Count, minSizeGB, dynamicMaxSizeGB, drive.Titles.Count);

        return filteredTitles;
    }

    public async Task<bool> RipTitlesAsync(AkDriveInfo drive, List<AkTitle> titles, string outputPath, bool skipIfExists = true)
    {
        if (titles.Count == 0)
        {
            _logger.LogWarning("No titles to rip");
            return false;
        }

        _logger.LogInformation("Starting to rip {TitleCount} titles from drive {DriveId}", titles.Count, drive.Id);
        _consoleOutput.ShowRippingProgress(titles.Count, drive.CDName);

        var allSuccessful = true;
        for (var i = 0; i < titles.Count; i++)
        {
            var title = titles[i];
            _logger.LogInformation("Ripping title {CurrentTitle}/{TotalTitles}: {TitleName} (ID: {TitleId})",
                i + 1, titles.Count, title.Name, title.Id);
            _consoleOutput.ShowCurrentTitleRipping(i + 1, titles.Count, title.Name, title.Id.ToString());
            var outputFilePath = System.IO.Path.Combine(outputPath, $"{title.Name}");
            if (skipIfExists && System.IO.File.Exists(outputFilePath))
            {


                _logger.LogInformation("Skipping title {TitleId}: {TitleName} (already exists at {OutputPath})",
                    title.Id, title.Name, outputFilePath);
                continue;

            }
            var success = await RipSingleTitleAsync(drive, title, outputPath);

            if (!success || !System.IO.File.Exists(outputFilePath))
            {
                _logger.LogError("Failed to rip title {TitleId}: {TitleName}", title.Id, title.Name);
                allSuccessful = false;
            }
            else
            {
                _logger.LogInformation("Successfully ripped title {TitleId}: {TitleName}", title.Id, title.Name);
            }
        }

        _logger.LogInformation("Ripping completed. Success: {Success}", allSuccessful);
        
        if (allSuccessful)
        {
            _consoleOutput.ShowRippingCompleted();
        }
        
        return allSuccessful;
    }

    public async Task<bool> RipDiscWithFilterAsync(AkDriveInfo drive, string outputPath, double minSizeGB = 3.0, double maxSizeGB = 12.0)
    {
        if (!await GetDiscInfoAsync(drive))
        {
            _logger.LogError("Failed to get disc information");
            return false;
        }

        var filteredTitles = FilterTitlesBySize(drive, minSizeGB, maxSizeGB);
        if (filteredTitles.Count == 0)
        {
            _logger.LogWarning("No titles found in size range {MinSize}-{MaxSize} GB", minSizeGB, maxSizeGB);
            return false;
        }

        return await RipTitlesAsync(drive, filteredTitles, outputPath);
    }

    public async Task<List<AkDriveInfo>> GetAvailableDrivesAsync()
    {
        _logger.LogInformation("Scanning for available drives");

        var arguments = "-r --progress=-same info";
        using var process = _processManager.CreateProcess(arguments);

        if (process == null)
        {
            _logger.LogError("Failed to create MakeMKV process for drive scan");
            return new List<AkDriveInfo>();
        }

        var drives = new Dictionary<string, AkDriveInfo>();

        try
        {
            process.Start();

            while (!process.StandardOutput.EndOfStream)
            {
                var line = await process.StandardOutput.ReadLineAsync();
                if (!string.IsNullOrEmpty(line))
                {
                    ParseDriveInfo(line, drives);
                }
            }

            await process.WaitForExitAsync();

            _logger.LogInformation("Found {DriveCount} available drives", drives.Count);
            return drives.Values.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning for drives");
            return new List<AkDriveInfo>();
        }
    }

    private async Task<bool> RipSingleTitleAsync(AkDriveInfo drive, AkTitle title, string outputPath)
    {
        var arguments = $"-r --progress=-same mkv disc:{drive.Id} {title.Id} \"{outputPath}\"";
        using var process = _processManager.CreateProcess(arguments);

        if (process == null)
        {
            return false;
        }

        _progressReporter.StartProgress($"Ripping {title.Name}");

        try
        {
            var success = await _processManager.ExecuteProcessAsync(
                process,
                outputHandler: _progressReporter.ParseProgressLine,
                errorHandler: line => _logger.LogWarning("MakeMKV error: {Error}", line)
            );

            return success;
        }
        finally
        {
            _progressReporter.CompleteProgress();
        }
    }

    private void ParseDriveInfo(string line, Dictionary<string, AkDriveInfo> drives)
    {
        if (!line.StartsWith("DRV:"))
            return;

        try
        {
            var split = line.Substring(4).Split(',');
            if (split[3] is "1" or "28" or "12")
            {


                var info = new AkDriveInfo()
                {
                    Id = RemoveInvalidPathChars(split[0]),
                    DriveName = RemoveInvalidPathChars(split[4]),
                    CDName = RemoveInvalidPathChars(split[5]),
                    DriveLetter = RemoveInvalidPathChars(split[6])
                };

                drives[info.Id] = info;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse drive info line: {Line}", line);
        }
    }
    public string RemoveInvalidPathChars(string folder)
    {
        return folder
            .Replace("\\", "")
            .Replace("/", "")
            .Replace(":", "")
            .Replace("*", "")
            .Replace("?", "")
            .Replace("<", "")
            .Replace(">", "")
            .Replace("|", "")
            .Replace("\"", "");
    }

}
