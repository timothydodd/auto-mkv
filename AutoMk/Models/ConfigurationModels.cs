using System;
using System.Collections.Generic;
using AutoMk.Interfaces;

namespace AutoMk.Models;

public class RipSettings : ISizeConfigurable
{
    public bool Flat { get; set; }
    public required string MakeMKVPath { get; set; }
    public List<string> IgnoreDrives { get; set; } = new();
    public List<NameItem> NameList { get; set; } = new();
    public required string Output { get; set; }
    public int MinTrackLength { get; set; } = 4800;
    public bool FilterBySize { get; set; } = true;
    public double MinSizeGB { get; set; } = 3.0;
    public double MaxSizeGB { get; set; } = 12.0;
    public bool EnableMediaIdentification { get; set; } = true;
    public string MediaStateDirectory { get; set; } = "state";
    public bool PreferPlexNaming { get; set; } = true;
    public bool EnableFileTransfer { get; set; } = false;
    public bool SkipExistingFiles { get; set; } = true;
    public FileTransferSettings? FileTransferSettings { get; set; }
    public PostRipSettings? PostRip { get; set; }
    public bool ManualMode { get; set; } = false;
    public ModeSelectionSetting ModeSelection { get; set; } = ModeSelectionSetting.Ask;
    public bool ShowConsoleLogging { get; set; } = false;
    public bool ShowProgressMessages { get; set; } = true;
}

public class NameItem
{
    public required string Name { get; set; }
    public required List<string> List { get; set; }
}

public class OmdbSettings
{
    public required string ApiKey { get; set; }
    public required string BaseUrl { get; set; }
}

public class FileTransferSettings
{
    public bool Enabled { get; set; } = false;
    public string TargetServiceUrl { get; set; } = "http://localhost:5000";
    public int MaxConcurrentTransfers { get; set; } = 2;
    public int TransferTimeoutMinutes { get; set; } = 240;
    public int BufferSizeBytes { get; set; } = 64 * 1024; // 64KB
    public bool DeleteAfterTransfer { get; set; } = false;
}

public class FileTransferRequest
{
    public string OriginalFileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string RelativeFilePath { get; set; } = string.Empty;
    public DateTime TransferTimestamp { get; set; }
}

public class PostRipSettings
{
    public long? DeleteFilesSmallerThan { get; set; }
}

public enum ModeSelectionSetting
{
    Ask = 1,
    AlwaysManual = 2,
    AlwaysAutomatic = 3
}
