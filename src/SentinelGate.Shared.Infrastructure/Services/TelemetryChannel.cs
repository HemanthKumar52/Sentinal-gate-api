using System.Runtime.CompilerServices;
using System.Threading.Channels;
using SentinelGate.Shared.Models.Entities;

namespace SentinelGate.Shared.Infrastructure.Services;

public class TelemetryChannel
{
    private readonly Channel<RequestLog> _channel;

    public TelemetryChannel(int maxBufferSize = 10_000)
    {
        _channel = Channel.CreateBounded<RequestLog>(new BoundedChannelOptions(maxBufferSize)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false
        });
    }

    public async ValueTask WriteAsync(RequestLog log, CancellationToken cancellationToken = default)
    {
        await _channel.Writer.WriteAsync(log, cancellationToken);
    }

    public bool TryWrite(RequestLog log)
    {
        return _channel.Writer.TryWrite(log);
    }

    public async IAsyncEnumerable<RequestLog> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return item;
        }
    }

    public ValueTask<RequestLog> ReadAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAsync(cancellationToken);
    }

    public int ReaderCount => _channel.Reader.Count;

    public void Complete()
    {
        _channel.Writer.Complete();
    }
}
