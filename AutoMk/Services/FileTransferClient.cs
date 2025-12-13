using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutoMk.Interfaces;
using AutoMk.Models;
using AutoMk.Utilities;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace AutoMk.Services;

public class FileTransferClient : IFileTransferClient
{
    private readonly HttpClient _httpClient;
    private readonly FileTransferSettings _settings;
    private readonly ILogger<FileTransferClient> _logger;
    private readonly IConsoleOutputService _consoleOutput;
    private readonly IProgressManager _progressManager;
    private readonly SemaphoreSlim _transferSemaphore;

    public FileTransferClient(
        HttpClient httpClient,
        FileTransferSettings settings,
        ILogger<FileTransferClient> logger,
        IConsoleOutputService consoleOutput,
        IProgressManager progressManager)
    {
        _httpClient = ValidationHelper.ValidateNotNull(httpClient);
        _settings = ValidationHelper.ValidateNotNull(settings);
        _logger = ValidationHelper.ValidateNotNull(logger);
        _consoleOutput = ValidationHelper.ValidateNotNull(consoleOutput);
        _progressManager = ValidationHelper.ValidateNotNull(progressManager);

        // Limit concurrent transfers
        _transferSemaphore = new SemaphoreSlim(_settings.MaxConcurrentTransfers, _settings.MaxConcurrentTransfers);

        // Configure HttpClient timeout for large file transfers
        _httpClient.Timeout = TimeSpan.FromMinutes(_settings.TransferTimeoutMinutes);
    }

    public async Task<bool> SendFileAsync(string relativePath, string filePath, CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            _logger.LogDebug("File transfer is disabled, skipping");
            return true;
        }

        if (!File.Exists(filePath))
        {
            _logger.LogError("File not found: {FilePath}", filePath);
            _consoleOutput.ShowError($"Transfer failed - File not found: {filePath}");
            return false;
        }

        // Check if target service is available with a short timeout
        _logger.LogInformation("Checking if file transfer service is available at: {ServiceUrl}", _settings.TargetServiceUrl);
        using var healthCheckCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        healthCheckCts.CancelAfter(TimeSpan.FromSeconds(10)); // 10 second timeout for health check

        if (!await IsServiceAvailableAsync(healthCheckCts.Token))
        {
            _logger.LogWarning("Target service is not available (health check failed or timed out), skipping file transfer for: {FilePath}", filePath);
            _consoleOutput.ShowError($"Transfer failed - Service unavailable at {_settings.TargetServiceUrl} (health check failed)");
            return false;
        }

        _logger.LogInformation("File transfer service is available, proceeding with transfer");

        await _transferSemaphore.WaitAsync(cancellationToken);

        Guid? progressTaskId = null;

        try
        {
            var fileName = Path.GetFileName(filePath);
            _logger.LogInformation("Starting transfer of {FileName} to {ServiceUrl}", fileName, _settings.TargetServiceUrl);
            _consoleOutput.ShowFileTransferStarted(fileName, _settings.TargetServiceUrl);

            var fileInfo = new FileInfo(filePath);
            var transferRequest = new FileTransferRequest
            {
                OriginalFileName = fileName,
                FileSizeBytes = fileInfo.Length,
                TransferTimestamp = DateTime.UtcNow,
                RelativeFilePath = relativePath
            };

            // Create progress task if ProgressManager is active
            if (_progressManager.IsActive)
            {
                progressTaskId = _progressManager.CreateProgressTask(
                    $"Transferring: {fileName}",
                    maxValue: 100,
                    category: "Transfer");
            }

            bool success;
            // Wrap the FileStream in its own scope so it gets disposed before we try to move the file
            {
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: _settings.BufferSizeBytes, useAsync: true);

                // Create progress callback for ProgressManager
                Action<long, long, double, TimeSpan>? progressCallback = null;
                if (progressTaskId.HasValue)
                {
                    var taskId = progressTaskId.Value;
                    progressCallback = (bytesTransferred, totalBytes, transferRateMBps, timeRemaining) =>
                    {
                        _progressManager.UpdateProgressBytes(taskId, bytesTransferred, totalBytes, transferRateMBps, timeRemaining);
                    };
                }

                success = await TransferFileAsync(fileStream, transferRequest, fileName, progressCallback, cancellationToken);
            } // FileStream is disposed here, releasing the file lock

            if (success)
            {
                // Mark progress task as complete
                if (progressTaskId.HasValue)
                {
                    _progressManager.CompleteProgressTask(progressTaskId.Value);
                }

                _logger.LogInformation("Successfully transferred {FileName}", fileName);
                _consoleOutput.ShowFileTransferCompleted(fileName);

                // Optionally delete the original file after successful transfer
                if (_settings.DeleteAfterTransfer)
                {
                    try
                    {
                        File.Delete(filePath);
                        _logger.LogInformation("Deleted original file: {FileName}", Path.GetFileName(filePath));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete original file: {FilePath}", filePath);
                    }
                }
                else
                {
                    // Calculate output root directory from file path and relative path
                    var fullPath = Path.GetFullPath(filePath);
                    var relativePathNormalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
                    var outputDirectory = fullPath.Substring(0, fullPath.Length - relativePathNormalized.Length).TrimEnd(Path.DirectorySeparatorChar);

                    // Preserve the relative folder structure in _moved folder
                    var movedFolder = Path.Combine(outputDirectory, "_moved");
                    var movedFilePath = Path.Combine(movedFolder, relativePath);
                    var movedFileDirectory = Path.GetDirectoryName(movedFilePath);

                    bool moveSucceeded = false;
                    int attempts = 0;
                    while (attempts < 10)
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(movedFileDirectory))
                            {
                                FileSystemHelper.EnsureDirectoryExists(movedFileDirectory);
                            }
                            File.Move(filePath, movedFilePath);
                            _logger.LogInformation("Moved file to: {MovedFilePath}", movedFilePath);

                            // Clean up empty directories after moving the file
                            var sourceDirectory = Path.GetDirectoryName(filePath);
                            CleanupEmptyDirectories(sourceDirectory, outputDirectory);
                            moveSucceeded = true;
                            break;
                        }
                        catch (Exception ex)
                        {
                            attempts++;
                            _logger.LogError(ex, "Failed to move file to: {MovedFilePath} (attempt {Attempts}/10)", movedFilePath, attempts);
                        }
                        Thread.Sleep(300);
                    }

                    // If the move failed after all retries, mark the overall operation as failed
                    if (!moveSucceeded)
                    {
                        _logger.LogError("Failed to move file to _moved folder after {Attempts} attempts: {FilePath}", attempts, filePath);
                        success = false;
                    }
                }
            }
            else
            {
                // Remove progress task on failure
                if (progressTaskId.HasValue)
                {
                    _progressManager.RemoveProgressTask(progressTaskId.Value);
                }
            }

            return success;
        }
        catch (Exception)
        {
            // Remove progress task on exception
            if (progressTaskId.HasValue)
            {
                _progressManager.RemoveProgressTask(progressTaskId.Value);
            }
            throw;
        }
        finally
        {
            _transferSemaphore.Release();
        }
    }

    public async Task<bool?> SendFileInBackground(string relativePath, string filePath)
    {
        if (!_settings.Enabled)
        {
            return null;
        }

        // Fire and forget - don't wait for completion
        return await SendFileAsync(relativePath, filePath);

    }

    private async Task<bool> IsServiceAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            var healthUrl = $"{_settings.TargetServiceUrl}/health";
            _logger.LogDebug("Making health check request to: {HealthUrl}", healthUrl);

            var response = await _httpClient.GetAsync(healthUrl, cancellationToken);
            var isAvailable = response.IsSuccessStatusCode;

            if (isAvailable)
            {
                _logger.LogDebug("Service health check successful - Status: {StatusCode}", response.StatusCode);
            }
            else
            {
                _logger.LogWarning("Service health check failed - Status: {StatusCode}", response.StatusCode);
            }

            return isAvailable;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Service availability check timed out after 10 seconds for: {ServiceUrl}", _settings.TargetServiceUrl);
            return false;
        }
        catch (HttpRequestException httpEx)
        {
            _logger.LogWarning(httpEx, "Service availability check failed due to HTTP error for: {ServiceUrl}", _settings.TargetServiceUrl);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Service availability check failed with unexpected error for: {ServiceUrl}", _settings.TargetServiceUrl);
            return false;
        }
    }

    private async Task<bool> TransferFileAsync(
        Stream fileStream,
        FileTransferRequest request,
        string fileName,
        Action<long, long, double, TimeSpan>? progressCallback,
        CancellationToken cancellationToken)
    {
        try
        {
            using var content = new MultipartFormDataContent();

            // Add metadata
            var metadataJson = JsonSerializer.Serialize(request);
            content.Add(new StringContent(metadataJson, Encoding.UTF8, "application/json"), "metadata");

            // Add file content with progress tracking
            var progressStream = new ProgressTrackingStreamContent(
                fileStream,
                request.FileSizeBytes,
                fileName,
                _consoleOutput,
                progressCallback);
            progressStream.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            content.Add(progressStream, "file", request.OriginalFileName);

            var response = await _httpClient.PostAsync($"{_settings.TargetServiceUrl}/upload", content, cancellationToken);

            // Ensure we end the progress line with a newline (only if not using ProgressManager)
            if (_settings.Enabled && !_progressManager.IsActive)
            {
                AnsiConsole.WriteLine();
            }

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogDebug("Transfer response: {ResponseContent}", responseContent);
                return true;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Transfer failed with status {StatusCode}: {ErrorContent}", response.StatusCode, errorContent);
                _consoleOutput.ShowError($"Transfer failed - HTTP {(int)response.StatusCode} ({response.StatusCode}): {errorContent}");
                return false;
            }
        }
        catch (HttpRequestException httpEx)
        {
            _logger.LogError(httpEx, "HTTP error during file transfer");
            _consoleOutput.ShowError($"Transfer failed - Connection error: {httpEx.Message}");
            return false;
        }
        catch (TaskCanceledException tcEx) when (tcEx.InnerException is TimeoutException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(tcEx, "File transfer timed out");
            _consoleOutput.ShowError($"Transfer failed - Request timed out after {_settings.TransferTimeoutMinutes} minutes");
            return false;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("File transfer was cancelled");
            _consoleOutput.ShowWarning("Transfer cancelled by user");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during file transfer");
            _consoleOutput.ShowError($"Transfer failed - {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private void CleanupEmptyDirectories(string? directory, string rootDirectory)
    {
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(rootDirectory))
            return;

        try
        {
            // Don't clean up the root directory itself or the _moved directory
            var fullDirPath = Path.GetFullPath(directory);
            var fullRootPath = Path.GetFullPath(rootDirectory);

            if (fullDirPath.Equals(fullRootPath, StringComparison.OrdinalIgnoreCase) ||
                fullDirPath.Contains("_moved", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Check if directory is empty (no files or subdirectories)
            if (Directory.Exists(directory) &&
                !Directory.GetFiles(directory).Any() &&
                !Directory.GetDirectories(directory).Any())
            {
                Directory.Delete(directory);
                _logger.LogDebug("Deleted empty directory: {Directory}", directory);

                // Recursively check parent directory
                var parentDirectory = Path.GetDirectoryName(directory);
                if (!string.IsNullOrEmpty(parentDirectory))
                {
                    CleanupEmptyDirectories(parentDirectory, rootDirectory);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not delete directory: {Directory}", directory);
        }
    }
}

/// <summary>
/// Custom HttpContent that tracks upload progress and calculates time remaining.
/// Supports both console output (legacy) and callback-based progress reporting (for ProgressManager).
/// </summary>
public class ProgressTrackingStreamContent : HttpContent
{
    private readonly Stream _stream;
    private readonly long _totalBytes;
    private readonly string _fileName;
    private readonly IConsoleOutputService _consoleOutput;
    private readonly Action<long, long, double, TimeSpan>? _progressCallback;
    private readonly Stopwatch _stopwatch;
    private long _bytesTransferred;
    private DateTime _lastUpdateTime;

    public ProgressTrackingStreamContent(
        Stream stream,
        long totalBytes,
        string fileName,
        IConsoleOutputService consoleOutput,
        Action<long, long, double, TimeSpan>? progressCallback = null)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _totalBytes = totalBytes;
        _fileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
        _consoleOutput = consoleOutput ?? throw new ArgumentNullException(nameof(consoleOutput));
        _progressCallback = progressCallback;
        _stopwatch = new Stopwatch();
        _lastUpdateTime = DateTime.UtcNow;
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        _stopwatch.Start();
        var buffer = new byte[64 * 1024]; // 64KB buffer
        int bytesRead;

        while ((bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await stream.WriteAsync(buffer, 0, bytesRead);
            _bytesTransferred += bytesRead;

            // Update progress every 500ms to avoid overwhelming the console
            var now = DateTime.UtcNow;
            if ((now - _lastUpdateTime).TotalMilliseconds >= 500)
            {
                try
                {
                    UpdateProgress();
                }
                catch
                {
                    // Ignore progress update errors - don't let UI issues interrupt the transfer
                }
                _lastUpdateTime = now;
            }
        }

        // Final progress update
        try
        {
            UpdateProgress();
        }
        catch
        {
            // Ignore progress update errors
        }
    }

    private void UpdateProgress()
    {
        if (_stopwatch.ElapsedMilliseconds > 0 && _bytesTransferred > 0)
        {
            // Calculate transfer rate in MB/s
            var elapsedSeconds = _stopwatch.ElapsedMilliseconds / 1000.0;
            var transferRateMBps = (_bytesTransferred / (1024.0 * 1024.0)) / elapsedSeconds;

            // Calculate time remaining
            var remainingBytes = _totalBytes - _bytesTransferred;
            var timeRemainingSeconds = remainingBytes / (transferRateMBps * 1024.0 * 1024.0);
            var timeRemaining = TimeSpan.FromSeconds(Math.Max(0, timeRemainingSeconds));

            // Use callback if provided (for ProgressManager)
            if (_progressCallback != null)
            {
                _progressCallback(_bytesTransferred, _totalBytes, transferRateMBps, timeRemaining);
            }

            // Also call console output (it will check if ProgressManager is active internally)
            _consoleOutput.ShowFileTransferProgress(_fileName, _bytesTransferred, _totalBytes, timeRemaining, transferRateMBps);
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _totalBytes;
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _stopwatch?.Stop();
        }
        base.Dispose(disposing);
    }
}
