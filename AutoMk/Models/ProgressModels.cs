using System;

namespace AutoMk.Models;

/// <summary>
/// Log level for progress manager messages
/// </summary>
public enum ProgressLogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Success
}

/// <summary>
/// Represents the state of a single progress item
/// </summary>
public class ProgressItemState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Description { get; set; } = string.Empty;
    public string? Category { get; set; }
    public double Value { get; set; }
    public double MaxValue { get; set; } = 100;
    public bool IsComplete { get; set; }
    public bool IsIndeterminate { get; set; }
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedTime { get; set; }

    // Additional metadata for file transfers
    public long? BytesTransferred { get; set; }
    public long? TotalBytes { get; set; }
    public double? TransferRateMBps { get; set; }
    public TimeSpan? TimeRemaining { get; set; }

    public double Percentage => MaxValue > 0 ? (Value / MaxValue) * 100 : 0;
    public TimeSpan Elapsed => DateTime.UtcNow - StartTime;
}

/// <summary>
/// Represents a log entry for the scrolling log display
/// </summary>
public class ProgressLogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Message { get; set; } = string.Empty;
    public ProgressLogLevel Level { get; set; } = ProgressLogLevel.Info;
    public bool IsMarkup { get; set; }
}

/// <summary>
/// Configuration options for the progress manager
/// </summary>
public class ProgressManagerOptions
{
    /// <summary>
    /// Maximum number of log entries to keep in the scrolling buffer
    /// </summary>
    public int MaxLogEntries { get; set; } = 50;

    /// <summary>
    /// Number of visible log lines in the display
    /// </summary>
    public int VisibleLogLines { get; set; } = 10;

    /// <summary>
    /// Refresh interval for the LiveDisplay in milliseconds
    /// </summary>
    public int RefreshIntervalMs { get; set; } = 100;

    /// <summary>
    /// Whether to show timestamps on log entries
    /// </summary>
    public bool ShowTimestamps { get; set; } = false;

    /// <summary>
    /// Whether to auto-remove completed progress tasks after a delay
    /// </summary>
    public bool AutoRemoveCompleted { get; set; } = true;

    /// <summary>
    /// Delay before auto-removing completed tasks (in milliseconds)
    /// </summary>
    public int AutoRemoveDelayMs { get; set; } = 2000;
}
