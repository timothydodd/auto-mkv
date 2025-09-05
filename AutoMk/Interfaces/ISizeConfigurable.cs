namespace AutoMk.Interfaces;

/// <summary>
/// Interface for classes that have size configuration properties (minimum and maximum size in GB)
/// </summary>
public interface ISizeConfigurable
{
    /// <summary>
    /// Minimum size in GB for filtering
    /// </summary>
    double MinSizeGB { get; set; }
    
    /// <summary>
    /// Maximum size in GB for filtering
    /// </summary>
    double MaxSizeGB { get; set; }
}