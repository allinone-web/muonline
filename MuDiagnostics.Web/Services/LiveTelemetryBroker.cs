using System.Collections.Concurrent;
using System.Threading.Channels;
using Client.Telemetry;

namespace MuDiagnostics.Web.Services;

public sealed class LiveTelemetryBroker
{
    private readonly ConcurrentDictionary<Guid, Channel<TelemetryEnvelope>> _subscribers = new();

    public LiveSubscription Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<TelemetryEnvelope>(new BoundedChannelOptions(16)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        _subscribers[id] = channel;
        return new LiveSubscription(id, channel.Reader, this);
    }

    public void Publish(TelemetryEnvelope envelope)
    {
        foreach (var subscriber in _subscribers.Values)
            subscriber.Writer.TryWrite(envelope);
    }

    private void Unsubscribe(Guid id)
    {
        if (_subscribers.TryRemove(id, out var channel))
            channel.Writer.TryComplete();
    }

    public sealed class LiveSubscription : IAsyncDisposable
    {
        private readonly Guid _id;
        private readonly LiveTelemetryBroker _owner;
        private int _disposed;

        internal LiveSubscription(Guid id, ChannelReader<TelemetryEnvelope> reader, LiveTelemetryBroker owner)
        {
            _id = id;
            Reader = reader;
            _owner = owner;
        }

        public ChannelReader<TelemetryEnvelope> Reader { get; }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _owner.Unsubscribe(_id);
            return ValueTask.CompletedTask;
        }
    }
}
