using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using AutoMk.Interfaces;

namespace AutoMk.Services;

/// <summary>
/// Unbounded channel-backed queue. Producers (scanners) never block on enqueue; consumers
/// (transfer workers) pull in FIFO order and exit cleanly when the channel is cancelled.
/// </summary>
public sealed class FileTransferQueue : IFileTransferQueue
{
    private readonly Channel<FileTransferJob> _channel = Channel.CreateUnbounded<FileTransferJob>(
        new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
        });

    public ValueTask EnqueueAsync(FileTransferJob job, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(job, cancellationToken);

    public IAsyncEnumerable<FileTransferJob> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}
