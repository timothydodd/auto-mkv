using System;
using System.Collections.Generic;

namespace AutoMk.Models;

public class AkTitle
{
    public required string Id { get; set; }
    public required string Length { get; set; }
    public long LengthInSeconds { get; set; }
    public required string Size { get; set; }
    public long SizeInBytes { get; set; }
    public double SizeInGB { get; set; }
    public required string Name { get; set; }
    public string? SourceFileName { get; set; }
    public int ChapterCount { get; set; }
}

public class AkDriveInfo
{
    public required string Id { get; set; }
    public required string DriveName { get; set; }
    public required string CDName { get; set; }
    public required string DriveLetter { get; set; }
    public Dictionary<string, AkTitle> Titles { get; set; } = new();
}

public class MakeMkvStatus
{
    public bool Converting { get; set; }
    public string? InputFile { get; set; }
    public string? OutputFile { get; set; }
    public float Percentage { get; set; }
    public float CurrentFps { get; set; }
    public float AverageFps { get; set; }
    public TimeSpan Estimated { get; set; }

    public override string ToString()
    {
        return !Converting
            ? "Idle"
            : $"{InputFile} -> {OutputFile} - {Percentage}%  {CurrentFps} fps.  {AverageFps} fps. avg.  {Estimated} time remaining";
    }
}