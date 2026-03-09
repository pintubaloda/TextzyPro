using System.Threading.Channels;
using Textzy.Api.Models;

namespace Textzy.Api.Services;

public class BroadcastQueueService
{
    private readonly Channel<BroadcastJob> _channel = Channel.CreateUnbounded<BroadcastJob>();

    public ValueTask EnqueueAsync(BroadcastJob job, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(job, ct);

    public IAsyncEnumerable<BroadcastJob> ReadAllAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);
}
