using System;

namespace AutoMk.Interfaces;

/// <summary>
/// Interface for console output service providing enhanced user feedback
/// </summary>
public interface IConsoleOutputService
{
    /// <summary>
    /// Outputs a disc detection message
    /// </summary>
    void ShowDiscDetected(string discName);

    /// <summary>
    /// Outputs a processing started message
    /// </summary>
    void ShowProcessingStarted(string discName);

    /// <summary>
    /// Outputs a media identification result
    /// </summary>
    void ShowMediaIdentified(string title, string type);

    /// <summary>
    /// Outputs ripping progress information
    /// </summary>
    void ShowRippingProgress(int totalTitles, string discName);

    /// <summary>
    /// Outputs current title being ripped
    /// </summary>
    void ShowCurrentTitleRipping(int currentTitle, int totalTitles, string titleName, string titleId);

    /// <summary>
    /// Outputs ripping completion message
    /// </summary>
    void ShowRippingCompleted();

    /// <summary>
    /// Outputs warning messages
    /// </summary>
    void ShowWarning(string message);

    /// <summary>
    /// Outputs error messages (always shown regardless of setting)
    /// </summary>
    void ShowError(string message);

    /// <summary>
    /// Outputs file rename information
    /// </summary>
    void ShowFileRenamed(string oldName, string newName);

    /// <summary>
    /// Outputs file organization completion
    /// </summary>
    void ShowOrganizationCompleted(int fileCount);

    /// <summary>
    /// Outputs file move destination
    /// </summary>
    void ShowFilesMovedTo(string destination);

    /// <summary>
    /// Outputs general information messages
    /// </summary>
    void ShowInfo(string message);

    /// <summary>
    /// Outputs success messages
    /// </summary>
    void ShowSuccess(string message);

    /// <summary>
    /// Outputs file transfer start message
    /// </summary>
    void ShowFileTransferStarted(string fileName, string destination);

    /// <summary>
    /// Outputs file transfer completion message
    /// </summary>
    void ShowFileTransferCompleted(string fileName);

    /// <summary>
    /// Outputs file transfer progress with time remaining
    /// </summary>
    void ShowFileTransferProgress(string fileName, long bytesTransferred, long totalBytes, TimeSpan timeRemaining, double transferRateMBps);
}