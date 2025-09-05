using System;
using AutoMk.Models;
using AutoMk.Utilities;
using Microsoft.Extensions.Logging;

namespace AutoMk.Services;

public class MakeMkvOutputParser
{
    private readonly ILogger<MakeMkvOutputParser> _logger;

    public MakeMkvOutputParser(ILogger<MakeMkvOutputParser> logger)
    {
        _logger = ValidationHelper.ValidateNotNull(logger);
    }

    public void ParseLine(string line, AkDriveInfo drive)
    {
        if (string.IsNullOrEmpty(line))
            return;

        if (line.StartsWith("TINFO:"))
        {
            ParseTitle(line, drive);
        }
        else if (line.Contains(".mpls was added as title #"))
        {
            ParseMplsAddition(line, drive);
        }
    }

    public void ParseTitle(string line, AkDriveInfo drive)
    {
        if (string.IsNullOrEmpty(line) || !line.StartsWith("TINFO:"))
            return;

        try
        {
            var parts = line.Substring(6).Split(',');
            if (parts.Length < 4)
                return;

            string titleId = parts[0];
            string propertyId = parts[1];
            string value = parts[3];

            if (!drive.Titles.TryGetValue(titleId, out var title))
            {
                title = new AkTitle
                {
                    Id = titleId,
                    Name = "",
                    Length = "",
                    Size = ""
                };
                drive.Titles[titleId] = title;
            }

            switch (propertyId)
            {
                case "9":  // Duration
                    title.Length = value;
                    var sections = value.Trim('"', ' ').Split(":");
                    if (sections.Length == 1)
                    {
                        title.LengthInSeconds = long.Parse(sections[0]);
                    }
                    else if (sections.Length == 3)
                    {
                        var hours = long.Parse(sections[0]);
                        var minutes = long.Parse(sections[1]);
                        var seconds = long.Parse(sections[2]);
                        title.LengthInSeconds = (hours * 3600) + (minutes * 60) + seconds;
                    }


                    break;
                case "10": // Size
                    title.Size = value;
                    // Parse size immediately
                    if (TryParseSizeInGB(value, out double sizeGB))
                    {
                        title.SizeInGB = sizeGB;
                        title.SizeInBytes = (long)(sizeGB * 1024 * 1024 * 1024);
                    }
                    break;
                case "27": // Name
                    title.Name = value.Trim('"', ' ');
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse title line: {Line}", line);
        }
    }

    public bool TryParseSizeInGB(string sizeString, out double sizeGB)
    {
        sizeGB = 0;

        if (string.IsNullOrEmpty(sizeString))
            return false;

        // Remove quotes and trim whitespace, then convert to uppercase for consistent parsing
        var cleanSize = sizeString.Trim('"', ' ').ToUpperInvariant();

        // Handle "4.7 GB" format (most common)
        if (cleanSize.EndsWith(" GB"))
        {
            var numberPart = cleanSize.Substring(0, cleanSize.Length - 3).Trim();
            if (double.TryParse(numberPart, out sizeGB))
                return true;
        }
        // Handle "4.7GB" format (no space)
        else if (cleanSize.EndsWith("GB"))
        {
            var numberPart = cleanSize.Substring(0, cleanSize.Length - 2);
            if (double.TryParse(numberPart, out sizeGB))
                return true;
        }
        // Handle "1024 MB" format
        else if (cleanSize.EndsWith(" MB"))
        {
            var numberPart = cleanSize.Substring(0, cleanSize.Length - 3).Trim();
            if (double.TryParse(numberPart, out double sizeMB))
            {
                sizeGB = sizeMB / 1024.0;
                return true;
            }
        }
        // Handle "1024MB" format (no space)
        else if (cleanSize.EndsWith("MB"))
        {
            var numberPart = cleanSize.Substring(0, cleanSize.Length - 2);
            if (double.TryParse(numberPart, out double sizeMB))
            {
                sizeGB = sizeMB / 1024.0;
                return true;
            }
        }
        // Handle plain number as bytes (fallback)
        else if (long.TryParse(cleanSize, out long bytes))
        {
            sizeGB = bytes / (1024.0 * 1024.0 * 1024.0);
            return true;
        }

        return false;
    }

    public void ParseMplsAddition(string line, AkDriveInfo drive)
    {
        try
        {
            // Parse lines like: "File 00042.mpls was added as title #3"
            var fileMatch = System.Text.RegularExpressions.Regex.Match(line, @"File (\d+\.mpls) was added as title #(\d+)");
            if (fileMatch.Success)
            {
                var sourceFileName = fileMatch.Groups[1].Value;
                var titleNumber = fileMatch.Groups[2].Value;

                // Find the title with this ID and set its source file name
                if (drive.Titles.TryGetValue(titleNumber, out var title))
                {
                    title.SourceFileName = sourceFileName;
                    _logger.LogDebug("Set source file name for title {TitleId}: {SourceFileName}", titleNumber, sourceFileName);
                }
                else
                {
                    // Create a placeholder title if it doesn't exist yet
                    var newTitle = new AkTitle
                    {
                        Id = titleNumber,
                        Name = "",
                        Length = "",
                        Size = "",
                        SourceFileName = sourceFileName
                    };
                    drive.Titles[titleNumber] = newTitle;
                    _logger.LogDebug("Created title {TitleId} with source file name: {SourceFileName}", titleNumber, sourceFileName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse MPLS addition line: {Line}", line);
        }
    }
}
