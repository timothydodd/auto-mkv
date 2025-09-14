namespace AutoMk.Interfaces;

/// <summary>
/// Interface for classes that have chapter configuration properties (minimum and maximum chapter count)
/// </summary>
public interface IChapterConfigurable
{
    /// <summary>
    /// Minimum chapter count for filtering
    /// </summary>
    int MinChapters { get; set; }

    /// <summary>
    /// Maximum chapter count for filtering
    /// </summary>
    int MaxChapters { get; set; }
}