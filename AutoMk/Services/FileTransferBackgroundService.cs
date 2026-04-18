using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMk.Interfaces;
using AutoMk.Models;
using AutoMk.Utilities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AutoMk.Services;

/// <summary>
/// Long-lived hosted service that drains <see cref="IFileTransferQueue"/> and performs HTTP
/// uploads via <see cref="IFileTransferClient"/>. Spawns <c>MaxConcurrentTransfers</c> worker
/// loops so transfers run in parallel up to that cap, independent of the main rip loop —
/// files enqueued at any point start transferring as soon as a worker is free.
/// </summary>
public sealed class FileTransferBackgroundService : BackgroundService
{
    private readonly IFileTransferQueue _queue;
    private readonly IFileTransferClient _client;
    private readonly FileTransferSettings _settings;
    private readonly ILogger<FileTransferBackgroundService> _logger;

    public FileTransferBackgroundService(
        IFileTransferQueue queue,
        IFileTransferClient client,
        FileTransferSettings settings,
        ILogger<FileTransferBackgroundService> logger)
    {
        _queue = ValidationHelper.ValidateNotNull(queue);
        _client = ValidationHelper.ValidateNotNull(client);
        _settings = ValidationHelper.ValidateNotNull(settings);
        _logger = ValidationHelper.ValidateNotNull(logger);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("File transfer disabled; transfer worker service is idle.");
            return Task.CompletedTask;
        }

        var workerCount = Math.Max(1, _settings.MaxConcurrentTransfers);
        _logger.LogInformation("Starting {WorkerCount} file transfer worker(s)", workerCount);

        var workers = Enumerable.Range(1, workerCount)
            .Select(id => Task.Run(() => WorkerLoopAsync(id, stoppingToken), stoppingToken))
            .ToArray();

        return Task.WhenAll(workers);
    }

    private async Task WorkerLoopAsync(int workerId, CancellationToken stoppingToken)
    {
        _logger.LogDebug("Transfer worker #{WorkerId} online", workerId);
        try
        {
            await foreach (var job in _queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await _client.SendFileAsync(job.RelativePath, job.FilePath, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Swallow so a single bad file doesn't take the worker offline. The client
                    // already surfaces per-file errors via ILogger + the dashboard log panel.
                    _logger.LogError(ex, "Transfer worker #{WorkerId} failed on {File}", workerId, job.FilePath);
                }
            }
        }
        catch (OperationCanceledException) { }
        _logger.LogDebug("Transfer worker #{WorkerId} exiting", workerId);
    }
}
