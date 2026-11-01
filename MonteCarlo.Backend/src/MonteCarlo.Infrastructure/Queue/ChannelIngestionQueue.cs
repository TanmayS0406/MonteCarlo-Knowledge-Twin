using System.Threading.Channels;
using MonteCarlo.Application.Abstractions;

namespace MonteCarlo.Infrastructure.Queue;

/// <summary>
/// In-process queue between the webhook and extraction, so slow LLM calls never block Graph notifications.
/// Swap for Azure Service Bus in production without touching the use cases.
/// </summary>
public sealed class ChannelIngestionQueue : IIngestionQueue
{
    private readonly Channel<IngestionJob> _channel = Channel.CreateBounded<IngestionJob>(new BoundedChannelOptions(1000)
    {
        FullMode = BoundedChannelFullMode.Wait
    });

    public ChannelReader<IngestionJob> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(IngestionJob job, CancellationToken ct = default) => _channel.Writer.WriteAsync(job, ct);
}
