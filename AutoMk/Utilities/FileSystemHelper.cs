using System.IO;

namespace AutoMk.Utilities;

/// <summary>
/// Provides helper methods for common file system operations
/// </summary>
public static class FileSystemHelper
{
    /// <summary>
    /// Ensures that a directory exists, creating it if necessary
    /// </summary>
    /// <param name="directoryPath">The directory path to ensure exists</param>
    /// <returns>True if the directory exists or was created successfully, false otherwise</returns>
    public static bool EnsureDirectoryExists(string? directoryPath)
    {
        if (string.IsNullOrEmpty(directoryPath))
            return false;

        try
        {
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ensures that the directory containing the specified file path exists
    /// </summary>
    /// <param name="filePath">The file path whose directory should be ensured to exist</param>
    /// <returns>True if the directory exists or was created successfully, false otherwise</returns>
    public static bool EnsureFileDirectoryExists(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return false;

        var directory = Path.GetDirectoryName(filePath);
        return EnsureDirectoryExists(directory);
    }

    /// <summary>
    /// Validates that a file path is not null or empty and the directory exists
    /// </summary>
    /// <param name="filePath">The file path to validate</param>
    /// <returns>True if the path is valid and directory exists, false otherwise</returns>
    public static bool IsValidFilePath(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return false;

        try
        {
            var directory = Path.GetDirectoryName(filePath);
            return !string.IsNullOrEmpty(directory) && Directory.Exists(directory);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Validates that a directory path is not null or empty and exists
    /// </summary>
    /// <param name="directoryPath">The directory path to validate</param>
    /// <returns>True if the path is valid and exists, false otherwise</returns>
    public static bool IsValidDirectoryPath(string? directoryPath)
    {
        return !string.IsNullOrEmpty(directoryPath) && Directory.Exists(directoryPath);
    }
}