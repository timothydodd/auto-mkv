using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MakeMkvEmulator;

/// <summary>
/// Root configuration for the MakeMKV emulator
/// </summary>
public class EmulatorConfiguration
{
    /// <summary>
    /// List of discs to emulate in sequence
    /// </summary>
    public List<EmulatedDisc> Discs { get; set; } = new();

    /// <summary>
    /// Default drive configuration
    /// </summary>
    public EmulatedDrive Drive { get; set; } = new()
    {
        Id = "0",
        DriveName = "EMULATOR_DRIVE",
        DriveLetter = "E:"
    };

    /// <summary>
    /// Current disc index (managed at runtime)
    /// </summary>
    [JsonIgnore]
    public int CurrentDiscIndex { get; set; } = 0;

    /// <summary>
    /// Get the currently loaded disc
    /// </summary>
    [JsonIgnore]
    public EmulatedDisc? CurrentDisc => CurrentDiscIndex < Discs.Count ? Discs[CurrentDiscIndex] : null;
}

/// <summary>
/// Represents an emulated CD/DVD/Blu-ray drive
/// </summary>
public class EmulatedDrive
{
    /// <summary>
    /// Drive ID (e.g., "0", "1")
    /// </summary>
    public string Id { get; set; } = "0";

    /// <summary>
    /// Drive name/model
    /// </summary>
    public string DriveName { get; set; } = "EMULATED_DRIVE";

    /// <summary>
    /// Drive letter (e.g., "E:")
    /// </summary>
    public string DriveLetter { get; set; } = "E:";
}

/// <summary>
/// Represents a single disc in the emulator
/// </summary>
public class EmulatedDisc
{
    /// <summary>
    /// Disc name (shown as CDName in MakeMKV)
    /// </summary>
    public string Name { get; set; } = "EMULATED_DISC";

    /// <summary>
    /// List of titles/tracks on this disc
    /// </summary>
    public List<EmulatedTitle> Titles { get; set; } = new();

    /// <summary>
    /// Whether this disc has been "inserted" (loaded)
    /// </summary>
    [JsonIgnore]
    public bool IsLoaded { get; set; } = false;
}

/// <summary>
/// Represents a single title/track on a disc
/// </summary>
public class EmulatedTitle
{
    /// <summary>
    /// Title ID (e.g., "0", "1", "2")
    /// </summary>
    public string Id { get; set; } = "0";

    /// <summary>
    /// Title name/filename
    /// </summary>
    public string Name { get; set; } = "title_t00.mkv";

    /// <summary>
    /// Duration in HH:MM:SS format
    /// </summary>
    public string Duration { get; set; } = "00:42:30";

    /// <summary>
    /// Size in GB
    /// </summary>
    public double SizeGB { get; set; } = 4.5;

    /// <summary>
    /// Number of chapters
    /// </summary>
    public int ChapterCount { get; set; } = 8;

    /// <summary>
    /// Source MPLS filename (optional, e.g., "00042.mpls")
    /// </summary>
    public string? SourceFileName { get; set; }

    /// <summary>
    /// How long the rip should take in seconds
    /// </summary>
    public int RipDurationSeconds { get; set; } = 30;

    /// <summary>
    /// Formatted size string for output
    /// </summary>
    [JsonIgnore]
    public string SizeString => $"{SizeGB:F1} GB";
}
