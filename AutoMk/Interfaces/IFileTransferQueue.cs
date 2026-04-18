using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AutoMk.Interfaces;

/// <summary>
/// Producer/consumer queue for file transfer jobs. <see cref="MediaMoverService"/> enqueues
/// files it discovers during scans; <see cref="AutoMk.Services.FileTransferBackgroundService"/>
/// consumes them from its own worker loops, independent of the main rip loop's cadence.
/// </summary>
public interface IFileTransferQueue
{
    ValueTask EnqueueAsync(FileTransferJob job, CancellationToken cancellationToken = default);

    IAsyncEnumerable<FileTransferJob> ReadAllAsync(CancellationToken cancellationToken);
}

public readonly record struct FileTransferJob(string RelativePath, string FilePath);
